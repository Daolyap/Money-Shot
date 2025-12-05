# MSI Installer

This directory contains the WiX Toolset configuration for building the Money Shot MSI installer.

## Overview

The MSI installer provides a proper Windows installation experience with:
- Installation to `Program Files\Money Shot`
- Desktop and Start Menu shortcuts
- Add/Remove Programs integration
- Proper upgrade/uninstall support

## Building Locally

To build the MSI installer locally on Windows:

1. Install .NET 8 SDK
2. Install WiX Toolset v5:
   ```powershell
   dotnet tool install --global wix --version 5.0.2
   wix extension add -g WixToolset.UI.wixext
   wix extension add -g WixToolset.Util.wixext
   ```

3. Publish the application:
   ```powershell
   dotnet publish ../MoneyShot/MoneyShot.csproj --configuration Release --output ./publish --self-contained false
   ```

4. Build the MSI:
   ```powershell
   wix build -arch x64 -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
     -d PublishDir="./publish" `
     -out MoneyShot.msi `
     Product.wxs
   ```

## Automatic Builds

The MSI is automatically built by the GitHub Actions workflow (`.github/workflows/build-msi.yml`) on:
- Push to main/master branch
- Pull requests
- Release creation
- Manual workflow dispatch

The MSI artifact is uploaded and available for download from the Actions tab.

## Configuration

The installer configuration is defined in `Product.wxs`:
- **UpgradeCode**: `A1B2C3D4-E5F6-7890-ABCD-EF1234567890` (must remain constant for upgrades)
- **Version**: 1.0.0 (should be updated for new releases)
- **Installation Directory**: `C:\Program Files\Money Shot`
- **Shortcuts**: Desktop and Start Menu

## Versioning

The version number is controlled by the `Version` property in `MoneyShot/MoneyShot.csproj` and the `Version` attribute in `Product.wxs`. These should be kept in sync.

## Notes

- The installer is 64-bit only (x64 architecture)
- Framework-dependent deployment (requires .NET 8 Runtime to be installed)
- Uses embedded CAB files for simpler distribution
- Supports major upgrades (newer versions can upgrade older ones)
