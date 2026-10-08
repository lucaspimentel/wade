#!/bin/sh
# Builds the C# version of wade (src/Wade) and installs it to ~/.local/bin/wade,
# replacing any existing install. Run ./install-local.sh to switch back to the Rust build.
# This script is removed when the C# version is retired.
#
# Requirements: .NET 10 SDK
#
# Usage: ./install-local-csharp.sh [-f|--force] [-u|--update]
#   -f, --force   Skip confirmation prompts and overwrite an existing installation.
#   -u, --update  Pull the latest changes from the remote before building.

set -eu

project_name=wade
project_file=src/Wade/Wade.csproj

force=0
update=0
for arg in "$@"; do
    case "$arg" in
        -f | --force) force=1 ;;
        -u | --update) update=1 ;;
        -h | --help)
            sed -n '2,10s/^# \{0,1\}//p' "$0"
            exit 0
            ;;
        *)
            echo "Error: unknown option: $arg" >&2
            exit 1
            ;;
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

# Reads a y/N answer from the terminal; returns 0 for yes.
confirm() {
    printf '%s (y/N) ' "$1"
    answer=''
    { read -r answer </dev/tty; } 2>/dev/null || true
    case "$answer" in [Yy]*) return 0 ;; *) return 1 ;; esac
}

script_dir=$(cd "$(dirname "$0")" && pwd)

# Check if the local clone is up-to-date with remote
info "Checking if repository is up-to-date..."
if branch=$(git -C "$script_dir" rev-parse --abbrev-ref HEAD 2>/dev/null) &&
    git -C "$script_dir" fetch origin "$branch" --quiet 2>/dev/null &&
    local_rev=$(git -C "$script_dir" rev-parse HEAD 2>/dev/null) &&
    remote_rev=$(git -C "$script_dir" rev-parse "origin/$branch" 2>/dev/null); then
    if [ "$local_rev" != "$remote_rev" ]; then
        behind=$(git -C "$script_dir" rev-list --count "HEAD..origin/$branch")
        ahead=$(git -C "$script_dir" rev-list --count "origin/$branch..HEAD")
        status=''
        [ "$behind" -gt 0 ] && status="$behind commit(s) behind"
        if [ "$ahead" -gt 0 ]; then
            [ -n "$status" ] && status="$status and "
            status="${status}$ahead commit(s) ahead of"
        fi
        warn "Warning: Local branch '$branch' is $status origin/$branch."

        if [ "$update" -eq 1 ]; then
            info "Pulling latest changes..."
            if ! git -C "$script_dir" pull --quiet; then
                error "Error: git pull failed. Resolve conflicts and try again."
                exit 1
            fi
            info "Repository updated successfully."
        elif [ "$force" -eq 0 ]; then
            if ! confirm "Continue anyway?"; then
                info "Installation cancelled. Run 'git pull' or use --update to update."
                exit 0
            fi
        fi
    else
        info "Repository is up-to-date with origin/$branch."
    fi
else
    warn "Warning: Could not check remote status."
    warn "Continuing with installation..."
fi

case "$(uname -s)" in
    MINGW* | MSYS* | CYGWIN*) exe_name="$project_name.exe" ;;
    *) exe_name="$project_name" ;;
esac
install_dir="$HOME/.local/bin"
install_path="$install_dir/$exe_name"

# Check for existing installation before building
already_installed=0
if [ -e "$install_path" ]; then
    already_installed=1
    if [ "$force" -eq 0 ]; then
        warn "$project_name is already installed at: $install_path"
        if ! confirm "Overwrite existing installation?"; then
            info "Installation cancelled."
            exit 0
        fi
    fi
fi

# Check for .NET SDK
if ! dotnet_version=$(dotnet --version 2>/dev/null); then
    error "Error: .NET SDK not found. Please install .NET 10 SDK or later."
    info "Download from: https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
fi
info ".NET SDK version: $dotnet_version"

# Build and publish the project
info "Building $project_name..."
publish_path="$script_dir/artifacts/publish"
if ! dotnet publish "$script_dir/$project_file" -c Release --output "$publish_path"; then
    error "Error: Failed to build project."
    exit 1
fi

# Verify executable exists
exe_path="$publish_path/$exe_name"
if [ ! -f "$exe_path" ]; then
    error "Error: Build succeeded but executable not found at: $exe_path"
    exit 1
fi

success "Build successful!"

# Remove old installation if present
if [ "$already_installed" -eq 1 ]; then
    warn "Removing existing installation..."
    rm -f "$install_path"
fi

# Create installation directory if it doesn't exist
if [ ! -d "$install_dir" ]; then
    info "Creating installation directory: $install_dir"
    mkdir -p "$install_dir"
fi

# Copy executable to installation directory
info "Installing to: $install_path"
cp "$exe_path" "$install_path"
chmod +x "$install_path"

printf '\n'
success "Installation complete!"
printf '\n'
info "To use $project_name, ensure ~/.local/bin is in your PATH:"
cat <<'EOF'

Add to your shell's rc file (~/.bashrc, ~/.zshrc, etc.):
    export PATH="$HOME/.local/bin:$PATH"

Then restart your shell or source the file.

EOF
info "Usage:"
echo "    wade              # open in current directory"
echo "    wade ~/Documents  # open in a specific directory"
