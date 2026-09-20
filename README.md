# Taskbar Widget for Spotify 🎵

Widget hiển thị thông tin bài hát và lời bài hát đồng bộ thời gian thực (Synced Lyrics) từ Spotify ngay trên thanh tác vụ (Taskbar) của Windows 11 & Windows 10. Tích hợp đầy đủ các nút điều khiển, không yêu cầu đăng nhập Spotify, không cần cấp quyền API.

---

### 📑 Mục lục / Table of Contents
- 🇻🇳 **[Hướng dẫn sử dụng (Tiếng Việt)](#-hướng-dẫn-sử-dụng-tiếng-việt)**
  - [Tính năng nổi bật](#-tính-năng-nổi-bật)
  - [Các phiên bản tải về](#-các-phiên-bản-tải-về)
  - [Hai chế độ hiển thị lời bài hát](#-hai-chế-độ-hiển-thị-lời-bài-hát-lyrics-modes)
  - [Hướng dẫn sử dụng chi tiết](#-hướng-dẫn-sử-dụng-chi-tiết)
  - [Menu chuột phải (Context Menu)](#-menu-chuột-phải-context-menu)
  - [Tự biên dịch từ mã nguồn (Build from Source)](#-tự-biên-dịch-từ-mã-nguồn)
  - [Khắc phục sự cố thường gặp](#-khắc-phục-sự-cố-thường-gặp)
- 🇬🇧 **[English Guide](#-english-guide)**
  - [Key Features](#-key-features)
  - [Download Options](#-download-options)
  - [Lyrics Display Modes](#-lyrics-display-modes)
  - [Detailed Usage](#-detailed-usage)
  - [Right-Click Context Menu](#-right-click-context-menu)
  - [Building from Source](#-building-from-source)
  - [Troubleshooting](#-troubleshooting)

---

# 🇻🇳 Hướng dẫn sử dụng (Tiếng Việt)

## ✨ Tính năng nổi bật

- **Tích hợp sâu vào Taskbar**: Tự động nhận diện thanh tác vụ Windows 11 & Windows 10, hiển thị mượt mà không che các biểu tượng ứng dụng và tự động ẩn khi chơi game hoặc xem video toàn màn hình (Fullscreen).
- **Lời bài hát đồng bộ thời gian thực (Karaoke Synced Lyrics)**:
  - Tự động tìm kiếm lời bài hát khớp theo từng giây với nguồn dữ liệu từ **LRCLIB** và **QQ Music**.
  - Hỗ trợ chuẩn xác cho nhạc Việt Nam (V-Pop, Rap Việt...), US-UK, K-Pop, C-Pop...
  - Cơ chế lọc dấu tiếng Việt và ký tự đặc biệt thông minh, hạn chế tối đa việc nhận nhầm lời.
  - Hiệu ứng cuộn mượt mà với tần số quét màn hình (60Hz / 120Hz+ qua `CompositionTarget.Rendering`).
- **Điều khiển đầy đủ**:
  - Phát / Tạm dừng (Play / Pause).
  - Bài trước / Bài kế tiếp (Previous / Next).
  - Nút Yêu thích (+) với trạng thái tích xanh chính xác khi bài hát đã nằm trong thư viện Spotify.
  - Chế độ Phát ngẫu nhiên (Tắt / Bật / Smart Shuffle có biểu tượng ngôi sao).
  - Chế độ Lặp lại (Tắt / Lặp danh sách / Lặp 1 bài).
  - Thanh chỉnh âm lượng độc lập kéo thả hoặc lăn chuột.
  - Thanh tiến trình phát (Progress bar) có thể nhấp chuột để tua bài (Seek).
- **Không cần đăng nhập**: Hoạt động qua Windows System Media Transport Controls (SMTC) nên **hoàn toàn an toàn**, không cần nhập tài khoản hay Client ID / Secret.

---

## 🚀 Các phiên bản tải về

Trong thư mục `publish/` (hoặc trên mục [Releases](https://github.com/typlex-mercur/spotify-taskbar-widget/releases)):

| Phiên bản | Tên file | Dung lượng | Mô tả |
|---|---|---|---|
| **Bản Portable Standalone** *(Khuyên dùng)* | `SpotifyTaskbarWidget-Standalone.exe` hoặc `.zip` | ~72 MB | **Click là chạy ngay (Zero-install)**. Đã tích hợp sẵn toàn bộ thư viện .NET 8 bên trong, chạy được trên mọi máy tính mà không cần cài đặt thêm bất kỳ phần mềm nào. |
| **Bản Siêu nhẹ (Lightweight)** | `SpotifyTaskbarWidget.exe` | ~25 MB | Dành cho máy **đã cài sẵn .NET 8 Desktop Runtime**. Khởi động siêu tốc, tối ưu hóa triệt để dung lượng và RAM. |

---

## 🎭 Hai chế độ hiển thị lời bài hát (Lyrics Modes)

Ứng dụng hỗ trợ 2 chế độ hiển thị linh hoạt:

1. **1 Window (Combined) — Chế độ gộp 1 thanh duy nhất (Mặc định)**:
   - Toàn bộ ảnh bìa, tên bài hát, lời bài hát chạy cuộn (Karaoke) và các nút điều khiển được gom gọn vào 1 thanh duy nhất trên Taskbar.
   - Thiết kế tinh gọn, hiện đại, tối ưu diện tích thanh tác vụ.
2. **2 Windows (Separate) — Chế độ 2 cửa sổ độc lập**:
   - Gồm 1 widget phát nhạc và 1 cửa sổ lời bài hát riêng biệt.
   - Bạn có thể kéo thả cửa sổ lời bài hát tới bất kỳ vị trí nào trên màn hình hoặc taskbar.
   - Hỗ trợ tùy chỉnh căn lề chữ: **Căn trái (Left)**, **Căn giữa (Center)** hoặc **Căn phải (Right)**.

---

## 🎮 Hướng dẫn sử dụng chi tiết

### 1. Khởi động ứng dụng
1. Tải và chạy file `SpotifyTaskbarWidget.exe` hoặc `SpotifyTaskbarWidget-Standalone.exe`.
2. Mở ứng dụng **Spotify Desktop** trên máy và phát một bài hát bất kỳ.
3. Widget sẽ lập tức xuất hiện trên Taskbar với đầy đủ thông tin bài hát và lời bài hát.

### 2. Các thao tác chuột tiện lợi
- **Mở nhanh Spotify**: Nhấp chuột trái vào ảnh bìa album hoặc tên bài hát để đưa cửa sổ Spotify lên màn hình.
- **Tua bài hát (Seek)**: Nhấp chuột trái vào vị trí bất kỳ trên thanh tiến trình mỏng màu xanh lá ở mép dưới widget.
- **Chỉnh âm lượng**: Di chuột vào nút âm lượng và lăn con lăn chuột lên/xuống để tăng/giảm âm lượng Spotify nhanh chóng.
- **Thêm vào Yêu thích**: Nhấp vào nút `+` để thả tim bài hát vào mục *Liked Songs*.

---

## ⚙️ Menu chuột phải (Context Menu)

Nhấp chuột phải vào bất kỳ vị trí trống nào trên widget để mở menu cài đặt:

- **Move / Resize (Di chuyển / Đổi kích thước)**:
  - Tích chọn mục này: Widget sẽ hiện viền cho phép bạn **kéo thả di chuyển** đến vị trí tùy ý trên taskbar, hoặc **kéo mép trái/phải** để thu hẹp/kéo dài chiều ngang.
  - Bỏ tích: Khóa cố định vị trí mới.
- **Reset position & size**: Đưa widget về lại vị trí tự động mặc định trên thanh taskbar.
- **Monitor**: Chọn hiển thị widget trên màn hình chính (Primary) hoặc các màn hình phụ (hỗ trợ hiển thị trên nhiều màn hình cùng lúc).
- **1 Window (Combined)**: Chuyển sang chế độ 1 thanh gộp duy nhất.
- **2 Windows (Separate)**: Chuyển sang chế độ 2 cửa sổ riêng biệt.
- **Lyrics (Lời bài hát)**:
  - `Show lyrics`: Bật hoặc tắt hiển thị lời bài hát.
  - `Alignment`: Căn lề lời bài hát (*Left*, *Center*, *Right*). *Mục này sẽ tự động ẩn khi ở chế độ 1 Window và chỉ xuất hiện khi đang dùng chế độ 2 Windows*.
- **Buttons (Nút bấm)**: Tùy chọn ẩn/hiện các nút điều khiển theo ý thích (Play/Pause, Favorite, Shuffle, Previous, Next, Repeat, Volume).
- **Progress bar**: Bật/tắt thanh tiến trình dưới đáy widget.
- **Size (Kích thước)**: Điều chỉnh cỡ hiển thị (*Small* / *Normal* / *Large*).
- **Brightness (Độ sáng / Độ mờ)**: Điều chỉnh độ trong suốt của widget từ 20% đến 100% (rất hữu ích cho màn hình OLED hoặc taskbar trong suốt).
- **Start with Windows (Khởi động cùng Windows)**: Tự động chạy widget mỗi khi bạn bật máy tính.
- **Quit (Sair / Thoát)**: Tắt ứng dụng widget.

> **Mẹo lưu trữ:** Toàn bộ cài đặt được lưu tự động tại thư mục:  
> `%APPDATA%\SpotifyTaskbarWidget\settings.json`

---

## 🛠️ Tự biên dịch từ mã nguồn

Yêu cầu máy tính đã cài đặt **.NET 8 SDK**. Mở PowerShell tại thư mục mã nguồn và chạy các lệnh sau:

### Build bản siêu nhẹ (Lightweight):
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

### Build bản Standalone (Độc lập 1 file, không cần runtime):
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "publish\temp"
Move-Item "publish\temp\SpotifyTaskbarWidget.exe" "publish\SpotifyTaskbarWidget-Standalone.exe" -Force
Remove-Item "publish\temp" -Recurse -Force
```

---

## ❓ Khắc phục sự cố thường gặp

1. **Widget không hiển thị thông tin bài hát?**
   - Đảm bảo bạn đang mở ứng dụng Spotify cho máy tính (Spotify Desktop App), không phải bản trình duyệt web.
   - Thử tạm dừng và phát lại bài hát trong Spotify để gửi tín hiệu SMTC đến Windows.
2. **Cảnh báo Windows Defender / SmartScreen khi tải về?**
   - Do file `.exe` là sản phẩm mã nguồn mở tự đóng gói và chưa có chữ ký số trả phí từ Microsoft, Windows có thể hiện cảnh báo "Windows protected your PC".
   - Bạn chỉ cần nhấp vào **More info** (Thông tin khác) và chọn **Run anyway** (Vẫn chạy).
3. **Bài hát không hiển thị lời?**
   - Một số bài hát quá mới hoặc bản thu âm không chính thức có thể chưa có dữ liệu đồng bộ thời gian trên LRCLIB và QQ Music. Ứng dụng sẽ tự động ẩn dòng lời bài hát khi không tìm thấy lời.

---
---

# 🇬🇧 English Guide

## ✨ Key Features

- **Seamless Taskbar Integration**: Integrates directly into Windows 11 & Windows 10 taskbars. Automatically positions itself next to system widgets, avoids overlapping center icons, and hides during fullscreen games or videos.
- **Real-Time Synced Lyrics (Karaoke)**:
  - Fetches accurate timed lyrics line-by-line using **LRCLIB** and **QQ Music**.
  - Comprehensive support for Vietnamese (V-Pop, Rap Viet), US-UK, K-Pop, C-Pop, and global tracks.
  - Smart title/artist diacritics stripping to prevent incorrect song matches.
  - High refresh rate rendering (60Hz / 120Hz+ via `CompositionTarget.Rendering`) for ultra-smooth scrolling text.
- **Complete Controls**:
  - Play / Pause, Previous, Next.
  - Favorite (+) button reflecting exact liked status from Spotify with a green checkmark.
  - Shuffle (Off / On / Smart Shuffle with star indicator).
  - Repeat (Off / Context / Track).
  - Integrated Volume Slider (mouse scroll supported).
  - Seekable Progress Bar.
- **No Login Required**: Uses the native Windows System Media Transport Controls (SMTC) API — no Spotify credentials, tokens, or API keys required.

---

## 🚀 Download Options

Files are located in the `publish/` directory or under GitHub [Releases](https://github.com/typlex-mercur/spotify-taskbar-widget/releases):

| Version | File | Size | Description |
|---|---|---|---|
| **Standalone Portable** *(Recommended)* | `SpotifyTaskbarWidget-Standalone.exe` or `.zip` | ~72 MB | **Zero-install**. Fully bundles the .NET 8 runtime. Runs on any Windows PC without prerequisites. |
| **Lightweight Version** | `SpotifyTaskbarWidget.exe` | ~25 MB | Designed for systems with **.NET 8 Desktop Runtime** installed. Extremely lightweight, fast startup, minimal RAM footprint. |

---

## 🎭 Lyrics Display Modes

1. **1 Window (Combined) — Default**:
   - Unifies the player controls, album art, track title, and synced lyrics into a single streamlined taskbar widget.
   - Lyrics scroll smoothly below the song title. Clean and compact.
2. **2 Windows (Separate)**:
   - Spawns a dedicated, standalone lyrics window alongside the main player widget.
   - Move the lyrics window anywhere on your screen or taskbar.
   - Supports text alignment: **Left**, **Center**, or **Right**.

---

## 🎮 Detailed Usage

### 1. Getting Started
1. Run `SpotifyTaskbarWidget.exe` or `SpotifyTaskbarWidget-Standalone.exe`.
2. Open Spotify Desktop and play any song.
3. The widget will appear on your taskbar immediately.

### 2. Mouse Controls
- **Focus Spotify**: Click on the album art or track text.
- **Seek Track**: Click anywhere along the bottom progress bar.
- **Adjust Volume**: Hover over the volume icon and use your mouse scroll wheel.
- **Favorite Song**: Click the `+` button to save the current track.

---

## ⚙️ Right-Click Context Menu

Right-click on any empty area of the widget to access settings:

- **Move / Resize**: Unlock the widget to drag it along the taskbar or adjust its width by dragging its edges. Untick to lock position.
- **Reset position & size**: Restores default automatic alignment.
- **Monitor**: Toggle widget appearance across primary and secondary displays.
- **1 Window (Combined)**: Switch to single combined widget mode.
- **2 Windows (Separate)**: Switch to two separate windows (player + lyrics).
- **Lyrics**:
  - `Show lyrics`: Enable or disable timed lyrics.
  - `Alignment`: Choose *Left*, *Center*, or *Right* text alignment (*Only shown when in 2 Windows mode*).
- **Buttons**: Toggle visibility of individual buttons (Play/Pause, Like, Shuffle, Prev, Next, Repeat, Volume).
- **Progress bar**: Toggle the bottom seek bar.
- **Size**: Change scale (*Small* / *Normal* / *Large*).
- **Brightness**: Adjust opacity slider (20% – 100%) for transparent or OLED taskbars.
- **Start with Windows**: Toggle automatic launch on Windows boot.
- **Quit**: Exit the widget.

> **Configuration Path:** User settings are saved at:  
> `%APPDATA%\SpotifyTaskbarWidget\settings.json`

---

## 🛠️ Building from Source

Requires the **.NET 8 SDK**. Run the following commands in PowerShell from the project root:

### Build Lightweight:
```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

### Build Standalone:
```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "publish\temp"
Move-Item "publish\temp\SpotifyTaskbarWidget.exe" "publish\SpotifyTaskbarWidget-Standalone.exe" -Force
Remove-Item "publish\temp" -Recurse -Force
```

---

## ❓ Troubleshooting

1. **No track information showing?**
   - Ensure the Spotify Desktop app is running and active.
   - Play/pause the track once to force Spotify to register media session events.
2. **Windows Defender / SmartScreen warning?**
   - Because community builds are not code-signed with expensive enterprise certificates, click **More info** -> **Run anyway**.
3. **No lyrics displayed?**
   - If a track does not exist on LRCLIB or QQ Music database, the lyrics area remains gracefully hidden.
