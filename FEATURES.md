# Money Shot - Features Overview

## 📸 Screenshot Capabilities

### Capture Modes
1. **Full Screen Capture**
   - Captures all connected monitors in a single screenshot
   - Preserves exact screen positioning
   - Hotkey: `Print Screen`
   - Accessible from: Main window, system tray menu

2. **Region Selection**
   - Interactive overlay for precise area selection
   - Visual feedback with red selection rectangle
   - Click and drag to select area
   - ESC to cancel selection
   - Minimum selection size: 10x10 pixels
   - Hotkey: `Ctrl + Print Screen`
   - Accessible from: Main window, system tray menu

3. **Multi-Monitor Support**
   - Automatic detection of all displays
   - Captures across multiple monitors
   - Handles different DPI settings per monitor

## 🎨 Annotation Tools

### Drawing Tools
- **Rectangle Tool**: Draw rectangular shapes with customizable colors and thickness
- **Circle Tool**: Draw circular/elliptical shapes
- **Arrow Tool**: Draw directional arrows (planned for full implementation)
- **Line Tool**: Draw straight lines
- **Number Tool**: Add sequential numbered markers (1, 2, 3...)
- **Text Tool**: Add text annotations (basic implementation)

### Customization Options
- **8 Preset Colors**: Red, Blue, Green, Yellow, Orange, Purple, Black, White
- **Line Thickness**: Configurable stroke width (default: 3px)
- **Transparent Fill**: Shapes have transparent fill by default

### Editing Features
- **Undo**: Remove the last added annotation
- **Layering**: Annotations stack in the order they're added
- **Real-time Preview**: See annotations as you draw them

## 💾 Save & Export

### Save Destinations
1. **Clipboard Only**
   - Instant copy to Windows clipboard
   - Paste directly into any application
   - One-click from editor toolbar

2. **File Only**
   - Save to specified location
   - Custom filename with timestamp
   - Configurable default path

3. **Both**
   - Saves to clipboard AND file simultaneously
   - Best for workflows requiring both

### File Formats
- **PNG** (default): Lossless compression, transparency support
- **JPG/JPEG**: Smaller file size, best for photos
- **BMP**: Uncompressed bitmap format
- **GIF**: Animated format support (basic)

### File Naming
- Auto-generated: `Screenshot_YYYY-MM-DD_HH-mm-ss.png`
- Manual naming via Save dialog
- Customizable default save location

## ⚙️ Settings & Configuration

### General Settings
- **Run on Windows Startup**: Automatically start Money Shot when Windows boots
- **Minimize to Tray**: Hide window to system tray instead of closing
- **Default Save Destination**: Clipboard, File, or Both
- **Default File Format**: PNG, JPG, or BMP
- **Default Save Path**: Custom directory for saved screenshots

### Hotkey Configuration
- Built-in hotkeys (currently fixed):
  - `Print Screen`: Full screen capture
  - `Ctrl + Print Screen`: Region selection
- Future: Custom hotkey assignment

### Application Behavior
- System tray integration
- Balloon notifications for minimize events
- Persistent settings storage in AppData
- Windows Registry integration for startup

## 🖥️ User Interface

### Main Window
- **Modern Dark Theme**: Eye-friendly dark UI (#2D2D30 background)
- **Large Action Buttons**: Easy-to-click capture buttons
- **Clear Visual Hierarchy**: Organized layout with proper spacing
- **Helpful Tooltips**: Hover hints for all buttons
- **Footer Tips**: Contextual help at bottom of window

### Editor Window
- **Responsive Canvas**: Scrollable for large screenshots
- **Organized Toolbar**: Grouped tools by function
  - Left: Drawing tools
  - Center: Color palette
  - Right: Actions (undo, save, copy)
- **Visual Tool Indicators**: Icons for each tool type
- **Dual-tone Design**: Dark toolbar (#2D2D30) with editor area (#1E1E1E)

### Settings Window
- **Categorized Options**: Grouped by functionality
- **Radio Buttons**: Clear selection for save destination
- **Browse Dialog**: Easy folder selection
- **Save/Cancel**: Clear action buttons

### System Tray
- **Always Accessible**: Right-click menu with all functions
- **Quick Capture**: Direct access to capture modes
- **Show/Hide**: Toggle main window visibility
- **Exit**: Clean application shutdown

## 🔧 Technical Features

### Performance
- **GDI+ Rendering**: Fast, reliable screen capture
- **WPF Hardware Acceleration**: Smooth annotation rendering
- **Efficient Memory Usage**: Proper disposal of resources

### Compatibility
- **Windows 11 Optimized**: Modern design language
- **Windows 10 Compatible**: Works on older versions
- **High DPI Aware**: Proper scaling on 4K displays
- **Multi-Monitor**: Handles various display configurations

### Security & Privacy
- **No Telemetry**: Zero data collection
- **Local Storage**: All settings saved locally
- **No Internet**: No network requests
- **Open Source**: Fully auditable code

### Reliability
- **Error Handling**: Graceful failure recovery
- **Settings Persistence**: Automatic settings save
- **Clean Shutdown**: Proper cleanup on exit

## 🚀 Workflow Examples

### Quick Screenshot to Clipboard
1. Press `Print Screen`
2. Editor opens with screenshot
3. Click "Copy" button
4. Paste into any application

### Annotated Tutorial Screenshot
1. Press `Ctrl + Print Screen`
2. Select region of interest
3. Use Rectangle tool to highlight areas
4. Add Number markers for steps
5. Add Text annotations for descriptions
6. Click "Save" to file

### Bug Report Screenshot
1. Capture full screen or region
2. Use Circle tool to highlight issue
3. Add Arrow pointing to problem
4. Add Text describing the bug
5. Save to both clipboard and file
6. Paste in bug tracker, file as backup

## 🎯 Use Cases

### Software Documentation
- Create step-by-step tutorials
- Annotate interface elements
- Add numbered sequences
- Highlight important areas

### Bug Reporting
- Capture error messages
- Annotate problem areas
- Add descriptive text
- Include system context

### Design Feedback
- Capture mockups or designs
- Add annotation comments
- Highlight specific elements
- Share via clipboard

### Education & Training
- Create instructional materials
- Add numbered steps
- Highlight key areas
- Build comprehensive guides

### Personal Use
- Save web content
- Capture receipts/confirmations
- Create image collections
- Share visual information

## 🔮 Planned Features

### Short-term
- [ ] Blur/Pixelate tool for privacy
- [ ] Enhanced arrow tool with adjustable heads
- [ ] Advanced text tool with font selection
- [ ] Freehand drawing tool
- [ ] Custom hotkey assignment

### Medium-term
- [ ] Screenshot history viewer
- [ ] Image effects (shadow, border, etc.)
- [ ] Multi-selection and grouping
- [ ] Copy/paste annotations
- [ ] Template system for common annotations

### Long-term
- [ ] Cloud storage integration
- [ ] OCR text recognition
- [ ] Video/GIF recording
- [ ] Collaboration features
- [ ] Plugin system
- [ ] Auto-update functionality
- [ ] MSI Installer package

## 💡 Tips & Tricks

1. **Fast Workflow**: Use hotkeys for instant capture
2. **System Tray**: Keep app running in background
3. **Both Mode**: Enable "save to both" for backup
4. **Numbered Steps**: Use Number tool for tutorials
5. **Color Coding**: Use different colors for categories
6. **Undo Freely**: Don't worry about mistakes
7. **Default Path**: Set up your preferred save location
8. **Startup**: Enable auto-start for always-ready access

## 📊 Comparison with Other Tools

### vs. Windows Snipping Tool
✅ More annotation tools
✅ Global hotkeys
✅ System tray integration
✅ Custom save destinations
✅ Better multi-monitor support

### vs. Greenshot
✅ Modern Windows 11 UI
✅ Simpler interface
✅ Better WPF rendering
❌ Fewer export options (planned)
❌ No plugins yet (planned)

### vs. ShareX
✅ Simpler, focused feature set
✅ Better for casual users
✅ Less overwhelming
❌ Fewer advanced features
❌ No automation (yet)

### vs. Flameshot
✅ Native Windows integration
✅ Better Windows 11 compatibility
✅ WPF-based smooth UI
≈ Similar annotation features

## 🎨 Design Philosophy

**Money Shot** is designed with these principles:

1. **Simplicity First**: Core features without overwhelming complexity
2. **Performance**: Fast capture and responsive editing
3. **Privacy**: No data collection, fully local
4. **Reliability**: Stable, predictable behavior
5. **Modern**: Contemporary UI matching Windows 11
6. **Accessible**: Easy for beginners, powerful for experts
7. **Open**: Source available for inspection and contribution

---

Every screenshot should be worth a thousand words... and dollars! 💰
