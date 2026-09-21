================================================================================
                    TASKBAR WIDGET FOR SPOTIFY
================================================================================

MỤC LỤC / TABLE OF CONTENTS:
  1. HƯỚNG DẪN SỬ DỤNG (TIẾNG VIỆT)
     - Giới thiệu & Tính năng nổi bật
     - Các phiên bản tải về
     - Hai chế độ hiển thị lời bài hát (Lyrics Modes)
     - Hướng dẫn thao tác chuột & Menu chuột phải
     - Cách sửa lỗi thường gặp
  2. ENGLISH GUIDE
     - Overview & Key Features
     - Download Options
     - Lyrics Display Modes
     - Controls & Right-Click Menu
     - Troubleshooting

================================================================================
                    1. HƯỚNG DẪN SỬ DỤNG (TIẾNG VIỆT)
================================================================================

[ GIỚI THIỆU & TÍNH NĂNG NỔI BẬT ]
* Tích hợp thanh Taskbar: Widget hiển thị trực tiếp trên thanh tác vụ Windows 11
  và Windows 10, tự động căn chỉnh không đè lên các icon ứng dụng.
* Lời bài hát đồng bộ (Karaoke Synced Lyrics):
  - Tự động tìm và chạy lời bài hát theo từng giây từ nguồn LRCLIB và QQ Music.
  - Hỗ trợ chuẩn xác nhạc Việt Nam (V-Pop, Rap Việt), US-UK, K-Pop, C-Pop,...
  - Animation mượt mà ở 60Hz / 120Hz+ qua CompositionTarget.
* Đầy đủ bộ điều khiển Spotify:
  - Play / Pause, Bài trước / Bài sau.
  - Thả tim Yêu thích (+ / tích xanh).
  - Trộn bài (Shuffle / Smart Shuffle ngôi sao) & Lặp lại (Repeat).
  - Chỉnh âm lượng bằng chuột và thanh tua nhạc (Seek bar).
* Hoàn toàn an toàn: Sử dụng Windows SMTC API, KHÔNG cần đăng nhập tài khoản
  Spotify và KHÔNG cần API key.

--------------------------------------------------------------------------------
[ CÁC PHIÊN BẢN TẢI VỀ ]

1. Bản Portable Standalone (Khuyên dùng):
   - Tên file: SpotifyTaskbarWidget-Standalone.exe (hoặc file .zip)
   - Dung lượng: ~72 MB
   - Đặc điểm: Click là chạy ngay (Zero-install). Đã tích hợp sẵn .NET 8,
     không cần cài thêm bất kỳ phần mềm hay runtime nào khác.

2. Bản Siêu nhẹ (Lightweight):
   - Tên file: SpotifyTaskbarWidget.exe
   - Dung lượng: ~25 MB
   - Đặc điểm: Dành cho máy tính đã cài sẵn .NET 8 Desktop Runtime.
     Khởi động siêu tốc, tối ưu hóa RAM và dung lượng.

--------------------------------------------------------------------------------
[ HAI CHẾ ĐỘ HIỂN THỊ LỜI BÀI HÁT ]

1. 1 Window (Combined) - Chế độ gộp 1 thanh (Mặc định):
   - Widget và lời bài hát gộp chung vào 1 thanh duy nhất trên taskbar.
   - Lời bài hát cuộn mượt mà ngay phía dưới tên bài hát. Tinh gọn, hiện đại.

2. 2 Windows (Separate) - Chế độ 2 cửa sổ độc lập:
   - Gồm 1 widget phát nhạc và 1 cửa sổ lời bài hát riêng biệt.
   - Bạn có thể kéo thả cửa sổ lời bài hát đến bất cứ vị trí nào tùy thích.
   - Cho phép căn lề chữ: Trái (Left), Giữa (Center) hoặc Phải (Right).

--------------------------------------------------------------------------------
[ HƯỚNG DẪN THAO TÁC CHUỘT ]

* Mở Spotify: Nhấp chuột trái vào ảnh bìa hoặc tên bài hát.
* Tua nhạc (Seek): Nhấp chuột vào thanh tiến trình màu xanh lá ở mép dưới.
* Chỉnh âm lượng: Rê chuột vào biểu tượng loa và lăn con lăn chuột lên/xuống.
* Thêm yêu thích: Nhấp nút [+] để lưu bài hát vào Liked Songs.
* Chuyển chế độ trộn: Nhấp nút Shuffle để đổi (Tắt -> Bật -> Smart Shuffle).

--------------------------------------------------------------------------------
[ MENU CHUỘT PHẢI (RIGHT-CLICK MENU) ]

Nhấp chuột phải vào khoảng trống bất kỳ trên widget để mở menu:

* Move / Resize (Di chuyển / Đổi kích thước):
  - Tích chọn: Kéo thả widget để di chuyển, hoặc rê chuột vào mép để kéo dài/thu hẹp.
  - Bỏ tích: Khóa cố định vị trí.
* Reset position & size: Khôi phục vị trí mặc định trên taskbar.
* Monitor: Chọn màn hình hiển thị widget (hỗ trợ nhiều màn hình).
* 1 Window (Combined): Chọn chế độ gộp 1 thanh duy nhất.
* 2 Windows (Separate): Chọn chế độ tách 2 cửa sổ riêng.
* Lyrics (Lời bài hát):
  - Show lyrics: Bật / tắt hiển thị lời.
  - Alignment: Căn lề Trái / Giữa / Phải (chỉ hiện khi ở chế độ 2 cửa sổ).
* Buttons: Bật / tắt hiển thị từng nút bấm theo ý thích.
* Progress bar: Bật / tắt thanh tiến trình dưới đáy widget.
* Size: Chọn kích thước Small / Normal / Large.
* Brightness: Chỉnh độ mờ/sáng của thanh widget (20% - 100%).
* Start with Windows: Tự khởi động cùng máy tính khi bật nguồn.
* Quit: Thoát widget.

* Cấu hình cá nhân được lưu tự động tại:
  %APPDATA%\SpotifyTaskbarWidget\settings.json

--------------------------------------------------------------------------------
[ KHẮC PHỤC SỰ CỐ THƯỜNG GẶP ]

1. Widget không hiện thông tin bài hát:
   - Hãy chắc chắn ứng dụng Spotify Desktop đang mở và đang phát nhạc.
   - Thử tạm dừng rồi phát lại bài hát trên Spotify.
2. Windows SmartScreen hiện cảnh báo khi tải về:
   - Do file mã nguồn mở chưa mua chữ ký số đắt đỏ từ Microsoft.
   - Bạn chỉ cần bấm "More info" (Thông tin khác) -> chọn "Run anyway" (Vẫn chạy).
3. Không thấy lời bài hát:
   - Một số bài hát quá mới hoặc bản cover/remix không chính thức có thể chưa
     có lời đồng bộ trên cơ sở dữ liệu LRCLIB hoặc QQ Music. Widget sẽ tự động
     ẩn dòng lời nếu không tìm thấy.
4. Lời bài hát bị lệch nhịp / trễ (Delay) ở một số bài:
   - Lời bài hát được lấy từ cơ sở dữ liệu mở cộng đồng (LRCLIB, QQ Music).
     Mốc thời gian (timestamp) của file LRC do người dùng tự gõ và căn chỉnh bằng tai.
   - Một số bài hát có thể bị lệch 1 - 2 giây do người tạo file LRC căn chỉnh
     chưa chuẩn, hoặc do bản nhạc trên Spotify có độ dài đoạn dạo đầu (intro)
     khác với bản MV/YouTube mà người tạo LRC dùng để canh lời. Đây là đặc tính
     dữ liệu từ nguồn cộng đồng của riêng bài hát đó, không phải lỗi của widget.


================================================================================
                    2. ENGLISH GUIDE
================================================================================

[ OVERVIEW & KEY FEATURES ]
* Taskbar Integration: Docks neatly into Windows 11 & Windows 10 taskbars
  without overlapping pinned app icons. Auto-hides on fullscreen games/videos.
* Real-Time Synced Lyrics (Karaoke):
  - Automatically fetches timed lyrics via LRCLIB & QQ Music.
  - Extensive song database covering Vietnamese, US-UK, K-Pop, C-Pop, etc.
  - Smooth scrolling powered by CompositionTarget (60Hz / 120Hz+).
* Comprehensive Spotify Controls:
  - Play / Pause, Previous / Next.
  - Add to Favorites (+ / green checkmark).
  - Shuffle / Smart Shuffle & Repeat modes.
  - Volume slider (with mouse scroll wheel support) & seekable progress bar.
* Secure & Private: Uses Windows SMTC API. No login, credentials, or API keys
  needed.

--------------------------------------------------------------------------------
[ DOWNLOAD OPTIONS ]

1. Standalone Portable (Recommended):
   - Filename: SpotifyTaskbarWidget-Standalone.exe (or .zip)
   - Size: ~72 MB
   - Zero installation required. Bundles full .NET 8 runtime. Runs anywhere.

2. Lightweight Version:
   - Filename: SpotifyTaskbarWidget.exe
   - Size: ~25 MB
   - Requires .NET 8 Desktop Runtime installed on the host PC. Fast and minimal.

--------------------------------------------------------------------------------
[ LYRICS DISPLAY MODES ]

1. 1 Window (Combined) - Default:
   - All controls, album art, track details, and synced lyrics are combined
     into a single elegant bar on the taskbar. Lyrics scroll below track title.

2. 2 Windows (Separate):
   - Main player widget + independent floating lyrics window.
   - Drag lyrics window anywhere across screens.
   - Text alignment choices: Left, Center, or Right.

--------------------------------------------------------------------------------
[ MOUSE CONTROLS ]

* Open Spotify: Click album art or song title.
* Seek: Click anywhere along the bottom green progress bar.
* Adjust Volume: Hover over the volume button and scroll mouse wheel up/down.
* Favorite: Click [+] to save track to Liked Songs.
* Shuffle: Click shuffle button to cycle (Off -> On -> Smart Shuffle).

--------------------------------------------------------------------------------
[ RIGHT-CLICK MENU OPTIONS ]

Right-click on any blank spot of the widget:

* Move / Resize: Unlock to drag widget or resize edges. Untick to lock.
* Reset position & size: Return to default auto-docked position.
* Monitor: Choose which monitor displays the widget (multi-monitor supported).
* 1 Window (Combined): Switch to compact single-bar mode.
* 2 Windows (Separate): Switch to separate player + lyrics windows.
* Lyrics:
  - Show lyrics: Toggle lyrics on/off.
  - Alignment: Left / Center / Right alignment (only in 2 Windows mode).
* Buttons: Show/hide specific player buttons.
* Progress bar: Toggle seek bar visibility.
* Size: Small / Normal / Large scale.
* Brightness: Adjust opacity (20% - 100%) for dark, light, or transparent bars.
* Start with Windows: Launch automatically when Windows starts.
* Quit: Exit application.

* User settings are stored at:
  %APPDATA%\SpotifyTaskbarWidget\settings.json

--------------------------------------------------------------------------------
[ TROUBLESHOOTING ]

1. Nothing is playing or showing:
   - Make sure Spotify Desktop application is running and playing audio.
   - Pause and unpause playback once.
2. Windows SmartScreen warning:
   - Click "More info", then choose "Run anyway".
3. No lyrics for certain songs:
   - If a song has no synced lyrics on LRCLIB/QQ Music, the lyric line is
     gracefully hidden.
4. Synced lyrics timing offset / delay on certain songs:
   - Lyrics are sourced from community databases (LRCLIB, QQ Music). The line
     timestamps in LRC files are contributed and timed manually by volunteers.
   - A few tracks may have a slight timing offset (1-2 seconds) if the contributor
     timed the lyrics against a different audio cut (e.g. music video vs. album release)
     or pressed timestamps with slight human delay. This is an artifact of the
     external community source for that specific song rather than a widget defect.

================================================================================
