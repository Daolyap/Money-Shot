# Developer Documentation

## Prerequisites

- .NET 8 SDK or later
- Windows 10/11 (for development and testing)
- Visual Studio 2022 or Visual Studio Code (recommended)

## Getting Started

### 1. Clone the Repository
```bash
git clone https://github.com/Daolyap/Money-Shot.git
cd Money-Shot
```

### 2. Restore Dependencies
```bash
dotnet restore MoneyShot/MoneyShot.csproj
```

### 3. Build the Project
```bash
# Debug build
dotnet build MoneyShot/MoneyShot.csproj --configuration Debug

# Release build
dotnet build MoneyShot/MoneyShot.csproj --configuration Release
```

### 4. Run the Application
```bash
dotnet run --project MoneyShot/MoneyShot.csproj
```

## Project Structure

### Core Services

#### ScreenshotService
Handles screen capture functionality:
- `CaptureFullScreen()` - Captures all screens
- `CaptureRegion(Rectangle)` - Captures specific region
- `CaptureScreen(int)` - Captures single screen by index

#### SaveService
Manages saving screenshots:
- `SaveToClipboard(BitmapSource)` - Copies to clipboard
- `SaveToFile(BitmapSource, string, string)` - Saves to file
- `SaveImage(BitmapSource, SaveDestination, string, string)` - Combined save

#### SettingsService
Handles application settings:
- `LoadSettings()` - Loads user settings from JSON
- `SaveSettings(AppSettings)` - Saves settings to JSON
- `SetStartupWithWindows(bool)` - Configures Windows startup

#### HotKeyService
Manages global keyboard shortcuts:
- `RegisterHotKey(modifiers, key, action)` - Registers hotkey
- `UnregisterHotKey(id)` - Removes hotkey
- Uses Win32 API for global hotkey registration

### UI Components

#### MainWindow
Main application interface:
- System tray integration
- Capture mode selection
- Settings access
- About information

#### EditorWindow
Screenshot annotation editor:
- Toolbar with annotation tools
- Color picker
- Canvas for drawing
- Save/copy functionality

#### RegionSelector
Full-screen overlay for region selection:
- Click and drag to select area
- Visual feedback with red rectangle
- ESC to cancel

#### SettingsWindow
Configuration interface:
- Save destination preferences
- File path and format selection
- Startup options
- Tray behavior

## Building for Release

### Create Portable Build
```bash
dotnet publish MoneyShot/MoneyShot.csproj \
  --configuration Release \
  --output ./publish \
  --runtime win-x64 \
  --self-contained false
```

### Create Self-Contained Build
```bash
dotnet publish MoneyShot/MoneyShot.csproj \
  --configuration Release \
  --output ./publish \
  --runtime win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true
```

## GitHub Actions

The project includes automated CI/CD workflows:

### Workflows

1. **Build and Release** (`.github/workflows/release.yml`)
   - Triggers automatically on every push to main/master branch
   - Builds both portable ZIP and MSI installer
   - Creates a GitHub release automatically with:
     - Version tag: `v{version}-build.{build_number}.{commit_sha}`
     - Both MSI and ZIP artifacts attached
     - Generated release notes
   - Marked as pre-release for development builds
   - **Versioning**: Build number is synchronized across all artifacts:
     - Assembly version: `{version}.{build_number}` (e.g., `1.0.0.123`)
     - File version: `{version}.{build_number}`
     - MSI version: `{version}.{build_number}`

2. **Build** (`.github/workflows/build.yml`)
   - Runs on pull requests for validation
   - Creates build artifacts for review
   - Ensures code builds successfully before merge
   - Uses build number for version synchronization

3. **Build MSI Installer** (`.github/workflows/build-msi.yml`)
   - Runs on pull requests for validation
   - Tests MSI installer creation
   - Can be manually triggered via workflow_dispatch
   - Uses build number for MSI version

### Release Process

Releases are fully automated:
- Every merge to main/master triggers a new release
- Version is extracted from `MoneyShot/MoneyShot.csproj`
- Build number from `GITHUB_RUN_NUMBER` is appended to assembly and file versions
- MSI installer version matches the executable version
- Build artifacts are automatically uploaded to GitHub Releases
- Users can download the latest build from the Releases page

### Version Synchronization

To ensure consistency, the build number is synchronized across:
- **Git Tag**: `v1.0.0-build.123.abc1234`
- **Assembly Version**: `1.0.0.123`
- **File Version**: `1.0.0.123`
- **MSI Version**: `1.0.0.123`

This is achieved by:
1. Extracting the base version from `MoneyShot.csproj`
2. Passing `/p:AssemblyVersion` and `/p:FileVersion` to `dotnet build`
3. Passing `-d ProductVersion` to the WiX toolset during MSI creation

## Adding New Features

### Adding a New Annotation Tool

1. Add enum value to `Models/AnnotationTool.cs`:
```csharp
public enum AnnotationTool
{
    // ... existing tools
    YourNewTool
}
```

2. Add button to `Views/EditorWindow.xaml`:
```xml
<Button Content="🎨 Your Tool" Tag="YourNewTool" Click="ToolButton_Click" ... />
```

3. Implement tool logic in `Views/EditorWindow.xaml.cs`:
```csharp
private Shape CreateYourTool()
{
    // Create and return your shape
}
```

4. Add case to tool switch in `Canvas_MouseDown`:
```csharp
AnnotationTool.YourNewTool => CreateYourTool(),
```

### Adding Settings

1. Add property to `Models/AppSettings.cs`:
```csharp
public YourType YourSetting { get; set; } = defaultValue;
```

2. Add UI control to `Views/SettingsWindow.xaml`

3. Load/save in `Views/SettingsWindow.xaml.cs`:
```csharp
// In LoadSettings()
YourControl.Value = _settings.YourSetting;

// In Save_Click()
_settings.YourSetting = YourControl.Value;
```

## Debugging

### Enable Debug Output
Set breakpoints in Visual Studio or use debug logging:

```csharp
#if DEBUG
System.Diagnostics.Debug.WriteLine("Your debug message");
#endif
```

### Common Issues

**Hotkeys not working:**
- Check if another application is using the same hotkey
- Ensure the window has been initialized before registering hotkeys

**Screenshots appear black:**
- Some applications use protected rendering
- Try capturing the entire screen instead

**High DPI issues:**
- Application manifest includes DPI awareness settings
- Test on different DPI scaling levels

## Testing

### Manual Testing Checklist
- [ ] Full screen capture works on all monitors
- [ ] Region selection captures correct area
- [ ] All annotation tools draw correctly
- [ ] Colors change when selected
- [ ] Undo works for all tools
- [ ] Save to clipboard works
- [ ] Save to file works
- [ ] Settings persist across restarts
- [ ] Hotkeys trigger captures
- [ ] System tray menu works
- [ ] Startup integration works

## Performance Considerations

- Screenshots are captured using GDI+ for maximum compatibility
- Large screenshots (multi-4K monitors) may take time to capture
- Annotation rendering uses WPF hardware acceleration when available

## Security Considerations

- No telemetry or data collection
- Settings stored locally in AppData
- No network requests
- Registry modifications only for startup integration (with user consent)

## Code Style

- Use C# 12 features where appropriate
- Follow Microsoft naming conventions
- Use nullable reference types
- Document public APIs with XML comments
- Keep methods focused and single-purpose

## Future Enhancements

See the roadmap in README.md for planned features.

## Getting Help

- Check existing issues on GitHub
- Review this documentation
- Examine the code - it's well-structured and commented
- Open a new issue if you're stuck
