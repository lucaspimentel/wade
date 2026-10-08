#!/bin/sh
# Downloads a pre-built wade binary from GitHub releases and installs it to ~/.local/bin/wade.
# No build tools are required, only curl or wget, tar and sha256sum or shasum.
#
# Usage: ./install-remote.sh [-f|--force] [version]
#   version       The version to install, e.g. "1.0.0". Defaults to "latest".
#   -f, --force   Skip confirmation prompts and overwrite an existing installation.
#
# One-liner:
#   curl -fsSL https://raw.githubusercontent.com/lucaspimentel/wade/main/install-remote.sh | sh
#   curl -fsSL https://raw.githubusercontent.com/lucaspimentel/wade/main/install-remote.sh | sh -s -- 1.0.0

set -eu

usage() {
    cat <<'EOF'
Usage: install-remote.sh [-f|--force] [version]
  version       The version to install, e.g. "1.0.0". Defaults to "latest".
  -f, --force   Skip confirmation prompts and overwrite an existing installation.
EOF
}

version=latest
force=0
for arg in "$@"; do
    case "$arg" in
        -f | --force) force=1 ;;
        -h | --help)
            usage
            exit 0
            ;;
        -*)
            echo "Error: unknown option: $arg" >&2
            usage >&2
            exit 1
            ;;
        *) version=$arg ;;
    esac
done

if [ -t 1 ]; then
    cyan='\033[36m' yellow='\033[33m' red='\033[31m' green='\033[32m' reset='\033[0m'
else
    cyan='' yellow='' red='' green='' reset=''
fi
info() { printf "${cyan}%s${reset}\n" "$*"; }
warn() { printf "${yellow}%s${reset}\n" "$*"; }
error() { printf "${red}%s${reset}\n" "$*" >&2; }
success() { printf "${green}%s${reset}\n" "$*"; }

# Reads a y/N answer from the terminal (stdin may be the piped script); returns 0 for yes.
confirm() {
    printf '%s (y/N) ' "$1"
    answer=''
    { read -r answer </dev/tty; } 2>/dev/null || true
    case "$answer" in [Yy]*) return 0 ;; *) return 1 ;; esac
}

# Determine the release asset for this platform
os=$(uname -s)
arch=$(uname -m)
if [ "$os" = Linux ] && [ "$arch" = x86_64 ]; then
    rid=linux-x64
else
    error "Error: Unsupported platform ($os $arch). Release binaries exist only for Linux x86_64 and Windows x64."
    info "To build from source on other platforms, install the Rust toolchain (https://rustup.rs) and run: ./install-local.sh"
    exit 1
fi

exe_name=wade
asset_name="wade-$rid.tar.gz"

if command -v curl >/dev/null 2>&1; then
    fetch() { curl -fsSL "$1"; }
    download() { curl -fsSL -o "$2" "$1"; }
elif command -v wget >/dev/null 2>&1; then
    fetch() { wget -qO- "$1"; }
    download() { wget -qO "$2" "$1"; }
else
    error "Error: curl or wget is required."
    exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
    sha256() { sha256sum "$1" | cut -d' ' -f1; }
elif command -v shasum >/dev/null 2>&1; then
    sha256() { shasum -a 256 "$1" | cut -d' ' -f1; }
else
    sha256() { :; }
fi

info "Installing wade for $rid..."

owner=lucaspimentel
repo=wade
base_url="https://github.com/$owner/$repo"

# Determine the release tag
if [ "$version" = latest ]; then
    info "Fetching latest release information from GitHub..."
    if ! release_json=$(fetch "https://api.github.com/repos/$owner/$repo/releases/latest"); then
        error "Error: Failed to fetch release information."
        exit 1
    fi
    tag=$(printf '%s\n' "$release_json" | sed -n 's/.*"tag_name": *"\([^"]*\)".*/\1/p' | head -n 1)
    if [ -z "$tag" ]; then
        error "Error: Could not read the latest release tag."
        exit 1
    fi
    actual_version=${tag#v}
    info "Latest version: $actual_version"
else
    actual_version=${version#v}
    tag="v$actual_version"
    info "Version: $actual_version"
fi

download_url="$base_url/releases/download/$tag/$asset_name"
checksums_url="$base_url/releases/download/$tag/checksums.txt"

install_dir="$HOME/.local/bin"
install_path="$install_dir/$exe_name"

# Create installation directory if it doesn't exist
if [ ! -d "$install_dir" ]; then
    info "Creating installation directory: $install_dir"
    mkdir -p "$install_dir"
fi

# Check for existing installation
if [ -e "$install_path" ]; then
    if [ "$force" -eq 0 ]; then
        warn "wade is already installed at: $install_path"
        if ! confirm "Overwrite existing installation?"; then
            info "Installation cancelled."
            exit 0
        fi
    fi
fi

# Create temporary directory for download
temp_dir=$(mktemp -d "${TMPDIR:-/tmp}/wade-install-XXXXXX")
trap 'rm -rf "$temp_dir"' EXIT

# Download archive
archive_path="$temp_dir/$asset_name"
info "Downloading from: $download_url"
if ! download "$download_url" "$archive_path"; then
    error "Error: Failed to download."
    error "Please verify the version exists at: $base_url/releases"
    exit 1
fi
success "Download complete!"

# Verify checksum
info "Verifying checksum..."
if checksums=$(fetch "$checksums_url" 2>/dev/null); then
    expected_hash=$(printf '%s\n' "$checksums" | grep -F "$asset_name" | head -n 1 | cut -d' ' -f1 || true)
    actual_hash=$(sha256 "$archive_path")
    if [ -z "$expected_hash" ]; then
        warn "Warning: No checksum found for '$asset_name' in checksums.txt. Skipping verification."
    elif [ -z "$actual_hash" ]; then
        warn "Warning: sha256sum or shasum not found. Skipping verification."
    elif [ "$(printf '%s' "$actual_hash" | tr 'A-F' 'a-f')" != "$(printf '%s' "$expected_hash" | tr 'A-F' 'a-f')" ]; then
        error "Error: Checksum verification failed!"
        error "  Expected: $expected_hash"
        error "  Actual:   $actual_hash"
        exit 1
    else
        success "Checksum verified."
    fi
else
    warn "Warning: Could not download checksums.txt. Skipping verification."
fi

# Extract archive
extract_path="$temp_dir/extract"
mkdir -p "$extract_path"
info "Extracting archive..."
tar -xzf "$archive_path" -C "$extract_path"

exe_path="$extract_path/$exe_name"
if [ ! -f "$exe_path" ]; then
    error "Error: Executable not found in archive at: $exe_path"
    exit 1
fi

# Replace the existing installation only after a successful download
info "Installing to: $install_path"
rm -f "$install_path"
cp "$exe_path" "$install_path"
chmod +x "$install_path"

printf '\n'
success "Installation complete! wade $actual_version is now installed."
printf '\n'
info "To use wade, ensure ~/.local/bin is in your PATH:"
cat <<'EOF'

Add to your shell's rc file (~/.bashrc, ~/.zshrc, etc.):
    export PATH="$HOME/.local/bin:$PATH"

Then restart your shell or source the file.

EOF
info "Usage:"
echo "    wade              # open in current directory"
echo "    wade ~/Documents  # open in a specific directory"
