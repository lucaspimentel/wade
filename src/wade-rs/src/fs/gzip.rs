//! A gzip reader with .NET `GZipStream` (Decompress) semantics over
//! `flate2`: concatenated members are read in sequence, a truncated stream
//! ends silently, bytes after a member that don't start another member are
//! ignored, and corrupt data (bad header, deflate data or CRC) is an
//! `InvalidData` error.

use std::io::{self, BufRead, Read};

use flate2::bufread::GzDecoder;

const GZIP_MAGIC: [u8; 2] = [0x1f, 0x8b];

pub struct GzipReader<R: Read> {
    decoder: Option<GzDecoder<PeekReader<R>>>,
}

impl<R: Read> GzipReader<R> {
    pub fn new(inner: R) -> Self {
        Self {
            decoder: Some(GzDecoder::new(PeekReader::new(inner))),
        }
    }
}

/// A buffered reader that can look `n` bytes ahead across refills.
struct PeekReader<R: Read> {
    inner: R,
    buf: Vec<u8>,
    pos: usize,
}

impl<R: Read> PeekReader<R> {
    fn new(inner: R) -> Self {
        Self { inner, buf: Vec::new(), pos: 0 }
    }

    /// The next `n` bytes, or fewer at end of stream.
    fn peek(&mut self, n: usize) -> io::Result<&[u8]> {
        while self.buf.len() - self.pos < n {
            let mut chunk = [0u8; 8192];
            let read = self.inner.read(&mut chunk)?;
            if read == 0 {
                break;
            }

            self.buf.drain(..self.pos);
            self.pos = 0;
            self.buf.extend_from_slice(&chunk[..read]);
        }

        let end = (self.pos + n).min(self.buf.len());
        Ok(&self.buf[self.pos..end])
    }
}

impl<R: Read> Read for PeekReader<R> {
    fn read(&mut self, out: &mut [u8]) -> io::Result<usize> {
        let available = self.fill_buf()?;
        let n = available.len().min(out.len());
        out[..n].copy_from_slice(&available[..n]);
        self.consume(n);
        Ok(n)
    }
}

impl<R: Read> BufRead for PeekReader<R> {
    fn fill_buf(&mut self) -> io::Result<&[u8]> {
        if self.pos >= self.buf.len() {
            self.buf.resize(8192, 0);
            let read = self.inner.read(&mut self.buf)?;
            self.buf.truncate(read);
            self.pos = 0;
        }

        Ok(&self.buf[self.pos..])
    }

    fn consume(&mut self, amount: usize) {
        self.pos = (self.pos + amount).min(self.buf.len());
    }
}

impl<R: Read> Read for GzipReader<R> {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        if buf.is_empty() {
            return Ok(0);
        }

        loop {
            let Some(decoder) = self.decoder.as_mut() else {
                return Ok(0);
            };

            match decoder.read(buf) {
                Ok(0) => {
                    // Member finished: continue only if another member follows
                    let mut inner = self.decoder.take().expect("decoder present").into_inner();
                    if inner.peek(2)? == GZIP_MAGIC {
                        self.decoder = Some(GzDecoder::new(inner));
                    } else {
                        return Ok(0);
                    }
                }
                Ok(n) => return Ok(n),
                Err(err) if err.kind() == io::ErrorKind::UnexpectedEof => {
                    // Truncated: GZipStream reports end of stream
                    self.decoder = None;
                    return Ok(0);
                }
                Err(err) => {
                    self.decoder = None;
                    return Err(io::Error::new(io::ErrorKind::InvalidData, err));
                }
            }
        }
    }
}

/// Reads until `buf` is full or the stream ends (C# `ReadFully`).
pub fn read_fully(reader: &mut (impl Read + ?Sized), buf: &mut [u8]) -> io::Result<usize> {
    let mut total = 0;

    while total < buf.len() {
        match reader.read(&mut buf[total..]) {
            Ok(0) => break,
            Ok(n) => total += n,
            Err(err) if err.kind() == io::ErrorKind::Interrupted => {}
            Err(err) => return Err(err),
        }
    }

    Ok(total)
}

#[cfg(test)]
mod tests {
    use std::io::{Read, Write};

    use flate2::Compression;
    use flate2::write::GzEncoder;

    use super::GzipReader;

    fn gz(data: &[u8]) -> Vec<u8> {
        let mut encoder = GzEncoder::new(Vec::new(), Compression::default());
        encoder.write_all(data).unwrap();
        encoder.finish().unwrap()
    }

    fn read_all(data: &[u8]) -> std::io::Result<Vec<u8>> {
        let mut out = Vec::new();
        GzipReader::new(data).read_to_end(&mut out)?;
        Ok(out)
    }

    #[test]
    fn reads_concatenated_members() {
        let mut data = gz(b"first\n");
        data.extend(gz(b"second\n"));
        assert_eq!(read_all(&data).unwrap(), b"first\nsecond\n");
    }

    #[test]
    fn ignores_trailing_garbage() {
        let mut data = gz(b"member\n");
        data.extend(b"garbage");
        assert_eq!(read_all(&data).unwrap(), b"member\n");
    }

    #[test]
    fn truncation_ends_silently_with_partial_data() {
        let text: Vec<u8> = (0..2000).map(|i| b'a' + (i % 26) as u8).collect();
        let data = gz(&text);
        let out = read_all(&data[..data.len() - 6]).unwrap();
        assert!(text.starts_with(&out));
    }

    #[test]
    fn bad_crc_and_non_gzip_are_invalid_data() {
        let mut data = gz(b"crc\n");
        let crc_at = data.len() - 8;
        data[crc_at] ^= 0xff;
        assert_eq!(read_all(&data).unwrap_err().kind(), std::io::ErrorKind::InvalidData);
        assert_eq!(read_all(b"plain text").unwrap_err().kind(), std::io::ErrorKind::InvalidData);
    }
}
