//! Stub of the metadata model from `src/Wade/Preview/MetadataTypes.cs`
//! (struct shapes only). The provider implementations are Phase 7; the
//! Properties overlay carries the parameter so Phase 7 is a drop-in.

/// Port of `MetadataEntry`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MetadataEntry {
    pub label: String,
    pub value: String,
}

/// Port of `MetadataSection`.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct MetadataSection {
    pub header: Option<String>,
    pub entries: Vec<MetadataEntry>,
}
