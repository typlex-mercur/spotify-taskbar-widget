# Spotify Taskbar Widget

A lightweight Windows 11 & 10 taskbar widget for Spotify. Displays currently playing track info and real-time synchronized lyrics (LRCLIB & QQ Music) directly on your taskbar, with full playback controls. No Spotify login or API keys required.

[English](#english-guide) | [Tiếng Việt](#hướng-dẫn-tiếng-việt)

---

## English Guide

### Features

- **Taskbar Integration**: Docks onto empty space on Windows 11 and 10 taskbars. Automatically positions itself, adapts to system DPI/resolution, and hides in fullscreen (games, videos).
- **Synced Lyrics**: Line-by-line timed lyrics from LRCLIB and QQ Music with diacritic-tolerant matching for Vietnamese, Asian, and international tracks.
- **Display Modes**:
  - **Combined (1 Window)**: Track info and scrolling lyrics packed into a single taskbar container.
  - **Separate (2 Windows)**: Main player plus an independent, draggable lyrics overlay with text alignment options.
- **Controls**: Play/pause, track navigation, volume scroll, seekable progress bar, shuffle modes (including Smart Shuffle), repeat, and library save (+).
- **Theme Support**: Automatically adapts to Windows light/dark mode and album art accent colors.
- **Zero Configuration**: Uses Windows System Media Transport Controls (SMTC) — no account credentials or Spotify Developer tokens needed.

### Download

Pre-built binaries are available under [Releases](https://github.com/typlex-mercur/spotify-taskbar-widget/releases):

| File | Type | Requirements | Description |
|---|---|---|---|
| `SpotifyTaskbarWidget-Standalone.exe` | Portable | None | Bundles .NET 8 runtime. Runs immediately without installation. |
| `SpotifyTaskbarWidget.exe` | Lightweight (~25 MB) | .NET 8 Desktop Runtime | Fast startup and smaller footprint for systems with .NET 8 installed. |

### Controls & Shortcuts

- **Focus Spotify**: Left-click album art or track title.
- **Seek**: Click anywhere along the bottom progress bar.
- **Adjust Volume**: Hover over the volume icon and use the mouse scroll wheel.
- **Context Menu**: Right-click empty area on the widget to access settings (position lock/unlock, monitor selection, display modes, button toggles, scale, and opacity).

Settings and logs are stored at `%APPDATA%\SpotifyTaskbarWidget\`.

### Building from Source

Requires the .NET 8 SDK:

```powershell
# Lightweight version (.NET 8 runtime required)
dotnet publish -c Release -r win-x64 --self-contained false -o publish

# Standalone version (single executable)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o publish
```

### Troubleshooting

- **No track info**: Ensure Spotify Desktop app is running (not Spotify Web Player). Play/pause once to prompt Windows media session registration.
- **SmartScreen prompt**: As an unsigned open-source binary, Windows may display a filter prompt. Click **More info** -> **Run anyway**.
- **Missing lyrics**: If a song is not in LRCLIB or QQ Music databases, the lyrics row gracefully falls back to a musical note indicator.

---

## Hướng dẫn (Tiếng Việt)

Widget hiển thị thông tin bài hát và lời bài hát đồng bộ thời gian thực (karaoke) từ Spotify ngay trên thanh Taskbar của Windows 11 và Windows 10. Hoạt động qua Windows SMTC, hoàn toàn không cần đăng nhập tài khoản hay cấu hình Spotify API.

### Tính năng

- Tích hợp trực tiếp lên Taskbar, tự động căn chỉnh vị trí tránh đè icon ứng dụng và tự ẩn khi mở game hoặc video toàn màn hình.
- Lời bài hát đồng bộ (Synced Lyrics) từ LRCLIB và QQ Music, lọc dấu thông minh cho nhạc Việt Nam (V-Pop, Rap), US-UK, K-Pop, C-Pop.
- 2 chế độ hiển thị:
  - **1 cửa sổ (Combined)**: Gom toàn bộ ảnh bìa, tên bài hát, lời karaoke cuộn và nút điều khiển vào một thanh gọn gàng trên taskbar.
  - **2 cửa sổ (Separate)**: Tách riêng thanh điều khiển và cửa sổ lời bài hát độc lập, hỗ trợ kéo thả tự do và chỉnh căn lề (trái / giữa / phải).
- Đầy đủ nút điều khiển: Play/Pause, Chuyển bài, Thả tim (+), Shuffle (kể cả Smart Shuffle), Lặp lại, Cuộn chuột chỉnh âm lượng, Nhấp chuột tua bài (Seek).
- Tự động đồng bộ giao diện Sáng / Tối theo Windows và đổi màu thanh tiến trình theo màu chủ đạo của bìa album.

### Bản cài đặt

Tải bản phát hành tại mục [Releases](https://github.com/typlex-mercur/spotify-taskbar-widget/releases):

- **Bản Standalone (`SpotifyTaskbarWidget-Standalone.exe`)**: Đã kèm sẵn runtime .NET 8, tải về nhấp đúp là chạy ngay.
- **Bản Siêu nhẹ (`SpotifyTaskbarWidget.exe`)**: Dung lượng khoảng 25 MB, yêu cầu máy đã cài sẵn .NET 8 Desktop Runtime.

### Thao tác sử dụng

- **Mở Spotify**: Bấm vào ảnh bìa hoặc tên bài hát để đưa cửa sổ Spotify lên màn hình.
- **Tua nhạc**: Nhấp chuột trái vào vị trí bất kỳ trên thanh tiến trình màu ở đáy widget.
- **Chỉnh âm lượng**: Rê chuột lên nút âm lượng và lăn con lăn chuột để tăng/giảm.
- **Menu cài đặt**: Chuột phải vào khoảng trống trên widget để di chuyển vị trí, chọn màn hình hiển thị, bật/tắt lyric, chỉnh cỡ chữ, độ mờ (hợp với màn hình OLED hoặc taskbar trong suốt).

File cấu hình và log được lưu tại: `%APPDATA%\SpotifyTaskbarWidget\`.

### Tự biên dịch từ mã nguồn

Yêu cầu máy tính có cài .NET 8 SDK:

```powershell
# Bản nhẹ (cần máy có sẵn .NET 8 Desktop Runtime)
dotnet publish -c Release -r win-x64 --self-contained false -o publish

# Bản Standalone độc lập
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o publish
```

### Xử lý sự cố thường gặp

- **Không nhận bài hát**: Phải dùng ứng dụng Spotify Desktop (không hỗ trợ bản web trên trình duyệt). Bấm Play/Pause một lần trên Spotify để kích hoạt session Windows.
- **Cảnh báo Windows SmartScreen**: File mã nguồn mở chưa mua chứng chỉ số của Microsoft, bấm **More info** -> **Run anyway**.
- **Không hiện lời bài hát**: Một số bài quá mới hoặc bản thu âm không chính thức chưa có trên cơ sở dữ liệu mở (LRCLIB/QQ Music), widget sẽ tự động ẩn dòng chữ và chỉ hiện nốt nhạc.
