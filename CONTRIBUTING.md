# Contributing to RetroSharp

## Development Setup

1. **Prerequisites**
   - .NET 10 SDK
   - Git

2. **Clone and Build**
   ```bash
   git clone <repository-url>
   cd RetroSharp
   dotnet restore
   dotnet build RetroSharp.sln
   ```

3. **Run Tests**
   ```bash
   dotnet test RetroSharp.sln
   ```

## CI/CD Pipeline

The project uses Azure DevOps Pipelines for continuous integration and deployment.

### Pipeline Configuration

- **File**: `azure-pipelines.yml`
- **Versioning**: GitVersion (configured in `GitVersion.yml`)
- **Deployment**: DotnetDeployer (configured in `dotnetdeployer.yml`)

### Build Stages

1. **Build and Test**
   - Restores dependencies
   - Builds the solution
   - Runs all unit tests
   - Publishes test results and code coverage

2. **Package and Publish**
   - Calculates version using GitVersion
   - Updates project version
   - Packages the RetroSharp CLI tool
   - Publishes to NuGet (master branch) or dry-run (other branches)
   - Tests tool installation

### Versioning Strategy

The project uses [GitVersion](https://gitversion.net/) with the GitHubFlow workflow:

- **master**: Production releases (no pre-release suffix)
- **feature branches** (any non-`master` branch, e.g. `feature/*`, `agent/*`): pre-release builds (`-<branch>.X`)

### Local Development Commands

```bash
# Build solution
dotnet build RetroSharp.sln -c Release

# Run tests
dotnet test RetroSharp.sln -c Release

# Generate version info
dotnet-gitversion

# Package tool (manual)
dotnet pack src/RetroSharp.Cli/RetroSharp.Cli.csproj -c Release -o packages/

# Install tool locally
dotnet tool install --global --add-source ./packages RetroSharp.Tool

# Use DotnetDeployer (if installed)
dotnetdeployer --config dotnetdeployer.yml --dry-run  # Dry run
dotnetdeployer --config dotnetdeployer.yml            # Publish to NuGet
```

### Setting up Azure DevOps

1. **Variable Groups**
   Create a variable group named `api-keys` with:
   - `NuGetApiKey`: Your NuGet.org API key

2. **Pipeline Setup**
   - Connect repository to Azure DevOps
   - Create new pipeline using existing `azure-pipelines.yml`
   - Configure the `api-keys` variable group

### Branch Strategy

GitHubFlow: `master` is the single long-lived branch and always holds stable, releasable code. All work happens on short-lived branches taken from `master` and merged back into it.

- `master`: stable, releasable at all times
- short-lived branches (e.g. `feature/*`, `agent/*`): all development, merged back into `master`

### Publishing Process

1. **Development**: Create a short-lived branch from `master`, then open a pull request back into `master`.
2. **Review and validate**: Build and tests must pass on the branch.
3. **Release**: Merge the branch into `master`; the pipeline publishes a stable package from `master`.

The pipeline automatically:
- Builds and tests all changes
- Creates pre-release packages for non-`master` branches
- Publishes stable releases from the `master` branch
- Tests the generated tool package

## Tool Architecture

RetroSharp shares a target-neutral frontend and then lowers directly through a
cartridge target:

```
Source Code (.rs) 
    ↓
Parser (ANTLR4) → AST
    ↓
TargetFrontendPreparation
    ↓
Game Boy lowerer ──or── NES lowerer
    ↓
Cartridge ROM (.gb / .nes)
```

For detailed architecture information, see `WARP.md`.
