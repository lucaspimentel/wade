//! Embeds the commit SHA for `--version`, matching the C# informational
//! version (`1.14.0+<full sha>`). Builds outside a git checkout leave
//! `WADE_COMMIT` unset and print the bare version, as .NET does without
//! source-control information.

use std::path::Path;
use std::process::Command;

fn git(args: &[&str]) -> Option<String> {
    let output = Command::new("git").args(args).output().ok()?;
    let text = String::from_utf8(output.stdout).ok()?;
    let text = text.trim();
    (output.status.success() && !text.is_empty()).then(|| text.to_string())
}

fn main() {
    println!("cargo:rerun-if-env-changed=WADE_COMMIT");

    if let Some(git_dir) = git(&["rev-parse", "--absolute-git-dir"]) {
        // Rebuild when HEAD moves: HEAD itself, the branch ref it names, and packed refs
        let git_dir = Path::new(&git_dir);
        println!("cargo:rerun-if-changed={}", git_dir.join("HEAD").display());

        let common_dir = git(&["rev-parse", "--path-format=absolute", "--git-common-dir"]);
        let common_dir = common_dir.as_deref().map_or(git_dir, Path::new);
        println!("cargo:rerun-if-changed={}", common_dir.join("packed-refs").display());

        if let Some(head_ref) = git(&["symbolic-ref", "-q", "HEAD"]) {
            println!("cargo:rerun-if-changed={}", common_dir.join(head_ref).display());
        }
    }

    if let Some(sha) = git(&["rev-parse", "HEAD"]) {
        println!("cargo:rustc-env=WADE_COMMIT={sha}");
    }
}
