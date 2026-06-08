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
   - Publishes to NuGet (main branch) or dry-run (other branches)
   - Tests tool installation

### Versioning Strategy

The project uses [GitVersion](https://gitversion.net/) with GitHubFlow workflow:

- **main**: Production releases (no pre-release suffix)
- **develop**: Alpha builds (`-alpha.X`)
- **feature/***: Feature builds (`-alpha.BranchName.X`)
- **hotfix/***: Hotfix builds (`-beta.X`)
- **release/***: Release candidates (`-beta.X`)

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

- `main`: Stable releases
- `develop`: Integration branch for new features
- `feature/*`: Feature development
- `hotfix/*`: Critical bug fixes
- `release/*`: Release preparation

### Publishing Process

1. **Development**: Work on feature branches, merge to `develop`
2. **Pre-release**: Create `release/*` branch from `develop`
3. **Release**: Merge `release/*` to `main`
4. **Hotfixes**: Create `hotfix/*` from `main`, merge back to both `main` and `develop`

The pipeline automatically:
- Builds and tests all changes
- Creates pre-release packages for non-main branches
- Publishes stable releases from `main` branch
- Tests the generated tool package

## Tool Architecture

RetroSharp follows a classic compiler architecture:

```
Source Code (.rs) 
    ↓
Parser (ANTLR4) → AST
    ↓
Semantic Analysis → Enriched AST
    ↓
Intermediate Code Generation → 3-Address Code
    ↓
Z80 Backend → Assembly
    ↓
Z80 Simulator (for testing)
```

For detailed architecture information, see `WARP.md`.
