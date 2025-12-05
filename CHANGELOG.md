# Changelog

All notable changes to Money Shot will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-12-05

### Initial Release 🎉

#### Added
- **Screenshot Capture**
  - Full screen capture with multi-monitor support
  - Region selection with interactive overlay
  - Global hotkey support (Print Screen, Ctrl+Print Screen)
  
- **Annotation Tools**
  - Rectangle tool with customizable colors
  - Circle/Ellipse tool
  - Line drawing tool
  - Arrow tool (basic implementation)
  - Number/counter tool for sequential markers
  - Text tool (basic implementation)
  - 8 preset colors: Red, Blue, Green, Yellow, Orange, Purple, Black, White
  - Undo functionality
  
- **Save & Export**
  - Save to clipboard
  - Save to file (PNG, JPG, BMP formats)
  - Save to both clipboard and file simultaneously
  - Auto-generated filenames with timestamps
  - Custom save location configuration
  
- **User Interface**
  - Modern dark-themed main window
  - Full-featured editor window with toolbar
  - Settings window for configuration
  - System tray integration
  - Balloon notifications
  
- **System Integration**
  - Windows startup integration (registry-based)
  - System tray menu with quick actions
  - Minimize to tray behavior
  - Global hotkey registration
  - Windows 11 DPI awareness
  
- **Settings & Configuration**
  - Persistent settings storage (JSON in AppData)
  - Default save destination configuration
  - Default file format selection
  - Custom save path
  - Startup preferences
  - Tray behavior preferences
  
- **Documentation**
  - Comprehensive README with installation instructions
  - Developer documentation (DEVELOPER.md)
  - Features overview (FEATURES.md)
  - Quick start guide (QUICKSTART.md)
  - MIT License
  
- **Build & CI/CD**
  - GitHub Actions workflow for automated builds
  - .NET 8 WPF project structure
  - Release artifact generation
  - Solution file for Visual Studio

### Technical Details
- Framework: .NET 8
- UI: WPF (Windows Presentation Foundation)
- Target: Windows 10/11 (net8.0-windows)
- Architecture: Modular service-based design
- Language: C# 12 with nullable reference types

### Known Limitations
- Blur/pixelate tool not yet implemented
- Text tool lacks font customization
- No custom hotkey assignment yet
- No screenshot history
- Arrow tool needs enhancement
- No MSI installer (ZIP distribution only)

## [Unreleased]

### Planned Features
- Blur and pixelate tool for privacy
- Enhanced text tool with font selection and sizing
- Freehand drawing tool
- Custom hotkey assignment
- Screenshot history viewer
- Image effects (drop shadow, borders)
- Auto-update functionality
- MSI installer package using WiX
- Copy/paste annotations
- Multi-selection and grouping
- Template system for common annotations

---

## Version History

**1.0.0** - Initial release with core screenshot and annotation features

---

For detailed feature information, see [FEATURES.md](FEATURES.md)

For installation and usage, see [README.md](README.md)
