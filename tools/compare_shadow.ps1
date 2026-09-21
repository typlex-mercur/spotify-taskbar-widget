Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="So sánh trực quan: Có DropShadow vs Không DropShadow (WPF Native)"
        Width="740" Height="580"
        WindowStartupLocation="CenterScreen"
        Background="#181818" Foreground="White"
        FontFamily="Segoe UI Variable Display, Segoe UI, sans-serif"
        ResizeMode="CanMinimize">
    <Window.Resources>
        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="White"/>
        </Style>
    </Window.Resources>
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <StackPanel Grid.Row="0" Margin="0,0,0,16">
            <TextBlock Text="SO SÁNH TRỰC QUAN HIỂN THỊ CHỮ LYRIC" FontSize="18" FontWeight="Bold" Foreground="#1ED760"/>
            <TextBlock Text="Xem trực tiếp trên engine đồ họa WPF để thấy rõ sự khác biệt của bóng đổ và độ nét chữ." FontSize="12" Foreground="#A0A0A0" Margin="0,4,0,0"/>
        </StackPanel>

        <!-- Comparison Cards -->
        <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto">
            <StackPanel>

                <!-- 1. Kích thước thật trên Taskbar (11.5pt) -->
                <Border Background="#242424" CornerRadius="8" Padding="16" Margin="0,0,0,14" BorderBrush="#333333" BorderThickness="1">
                    <StackPanel>
                        <Grid Margin="0,0,0,12">
                            <TextBlock Text="1. KÍCH THƯỚC THỰC TẾ TRÊN TASKBAR (Font 11.5pt)" FontWeight="SemiBold" FontSize="13" Foreground="#E0E0E0" VerticalAlignment="Center"/>
                            <Button x:Name="BtnToggleAnim" Content="▶ Chạy thử cuộn chữ (Animation)" HorizontalAlignment="Right" Padding="10,4" Background="#2E4A38" Foreground="#1ED760" BorderThickness="0" Cursor="Hand" FontWeight="SemiBold" FontSize="11"/>
                        </Grid>
                        
                        <!-- Style A: Trước đây -->
                        <Border Background="#121212" CornerRadius="6" Padding="12,10" Margin="0,0,0,8" BorderBrush="#3A3A3A" BorderThickness="1">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="200"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <TextBlock Text="TRƯỚC ĐÂY (Có DropShadow)" FontSize="11" FontWeight="Bold" Foreground="#FF6B6B"/>
                                    <TextBlock Text="BlurRadius=4 | GPU ~3.4%" FontSize="10" Foreground="#888888"/>
                                </StackPanel>
                                <Canvas Grid.Column="1" Height="20" ClipToBounds="True">
                                    <TextBlock x:Name="TextShadow" Text="♪ Em của ngày hôm qua úa u... Xin hãy xua tan đi em, bóng tối nơi này"
                                               FontSize="11.5" FontWeight="SemiBold" Foreground="White"
                                               Canvas.Left="0" Canvas.Top="1">
                                        <TextBlock.RenderTransform>
                                            <TranslateTransform x:Name="TransShadow" X="0" Y="0"/>
                                        </TextBlock.RenderTransform>
                                        <TextBlock.Effect>
                                            <DropShadowEffect Color="#000000" BlurRadius="4" ShadowDepth="1" Opacity="0.85" Direction="270"/>
                                        </TextBlock.Effect>
                                    </TextBlock>
                                </Canvas>
                            </Grid>
                        </Border>

                        <!-- Style B: Hiện tại -->
                        <Border Background="#121212" CornerRadius="6" Padding="12,10" Margin="0,0,0,8" BorderBrush="#1ED760" BorderThickness="1">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="200"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <TextBlock Text="HIỆN TẠI (Sắc nét Crisp)" FontSize="11" FontWeight="Bold" Foreground="#1ED760"/>
                                    <TextBlock Text="Không Blur | GPU ~0.0%" FontSize="10" Foreground="#888888"/>
                                </StackPanel>
                                <Canvas Grid.Column="1" Height="20" ClipToBounds="True">
                                    <TextBlock x:Name="TextCrisp" Text="♪ Em của ngày hôm qua úa u... Xin hãy xua tan đi em, bóng tối nơi này"
                                               FontSize="11.5" FontWeight="SemiBold" Foreground="White"
                                               Canvas.Left="0" Canvas.Top="1">
                                        <TextBlock.RenderTransform>
                                            <TranslateTransform x:Name="TransCrisp" X="0" Y="0"/>
                                        </TextBlock.RenderTransform>
                                    </TextBlock>
                                </Canvas>
                            </Grid>
                        </Border>

                        <!-- Style C: Bóng cứng 0px (Tùy chọn) -->
                        <Border Background="#121212" CornerRadius="6" Padding="12,10" BorderBrush="#3A3A3A" BorderThickness="1">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="200"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>
                                <StackPanel Grid.Column="0">
                                    <TextBlock Text="LỰA CHỌN THÊM (Bóng cứng 0px)" FontSize="11" FontWeight="Bold" Foreground="#64B5F6"/>
                                    <TextBlock Text="BlurRadius=0 | GPU ~0.0%" FontSize="10" Foreground="#888888"/>
                                </StackPanel>
                                <Canvas Grid.Column="1" Height="20" ClipToBounds="True">
                                    <TextBlock x:Name="TextHard" Text="♪ Em của ngày hôm qua úa u... Xin hãy xua tan đi em, bóng tối nơi này"
                                               FontSize="11.5" FontWeight="SemiBold" Foreground="White"
                                               Canvas.Left="0" Canvas.Top="1">
                                        <TextBlock.RenderTransform>
                                            <TranslateTransform x:Name="TransHard" X="0" Y="0"/>
                                        </TextBlock.RenderTransform>
                                        <TextBlock.Effect>
                                            <DropShadowEffect Color="#000000" BlurRadius="0" ShadowDepth="1" Opacity="0.9" Direction="270"/>
                                        </TextBlock.Effect>
                                    </TextBlock>
                                </Canvas>
                            </Grid>
                        </Border>
                    </StackPanel>
                </Border>

                <!-- 2. Phóng to 300% (Để soi chi tiết cạnh nét chữ) -->
                <Border Background="#242424" CornerRadius="8" Padding="16" Margin="0,0,0,14" BorderBrush="#333333" BorderThickness="1">
                    <StackPanel>
                        <TextBlock Text="2. PHÓNG TO 300% (Chi tiết viền &amp; nét chữ)" FontWeight="SemiBold" FontSize="13" Foreground="#E0E0E0" Margin="0,0,0,12"/>
                        
                        <!-- Zoomed Trước đây -->
                        <Border Background="#101010" CornerRadius="6" Padding="14,10" Margin="0,0,0,10" BorderBrush="#333333" BorderThickness="1">
                            <StackPanel>
                                <TextBlock Text="[Trước đây] Bóng mờ 4px tạo quầng xám/đen lem nhẹ quanh nét chữ nhỏ:" FontSize="11" Foreground="#FF8A80" Margin="0,0,0,6"/>
                                <TextBlock Text="♪ Nét chữ có quầng mờ đen (Blur)"
                                           FontSize="22" FontWeight="SemiBold" Foreground="White">
                                    <TextBlock.Effect>
                                        <DropShadowEffect Color="#000000" BlurRadius="6" ShadowDepth="1.5" Opacity="0.85" Direction="270"/>
                                    </TextBlock.Effect>
                                </TextBlock>
                            </StackPanel>
                        </Border>

                        <!-- Zoomed Hiện tại -->
                        <Border Background="#101010" CornerRadius="6" Padding="14,10" BorderBrush="#333333" BorderThickness="1">
                            <StackPanel>
                                <TextBlock Text="[Hiện tại] Nét chữ trắng nguyên bản, viền pixel sắc cạnh, đồng nhất với Taskbar:" FontSize="11" Foreground="#B9F6CA" Margin="0,0,0,6"/>
                                <TextBlock Text="♪ Nét chữ sắc nét nguyên bản (Crisp)"
                                           FontSize="22" FontWeight="SemiBold" Foreground="White"/>
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </Border>

                <!-- 3. Tóm tắt kỹ thuật -->
                <Border Background="#1A251D" CornerRadius="8" Padding="14" BorderBrush="#2E4A38" BorderThickness="1">
                    <StackPanel>
                        <TextBlock Text="💡 TỔNG KẾT:" FontWeight="Bold" FontSize="12" Foreground="#1ED760" Margin="0,0,0,4"/>
                        <TextBlock Text="• [Trước đây] Có DropShadow: Giúp chữ tách khỏi nền nếu taskbar có hình nền sáng loang lổ, nhưng ngốn 3.4% GPU khi cuộn do GPU phải tính toán làm mờ 120 FPS liên tục." FontSize="11.5" Foreground="#D0D0D0" TextWrapping="Wrap" Margin="0,2"/>
                        <TextBlock Text="• [Hiện tại] Không DropShadow: Chữ sắc cạnh, sáng rõ, giống hệt font chữ của đồng hồ/icon Windows Taskbar, GPU rơi về 0%." FontSize="11.5" Foreground="#D0D0D0" TextWrapping="Wrap" Margin="0,2"/>
                        <TextBlock Text="• [Lựa chọn thêm] Bóng cứng 0px: Nếu bạn muốn vừa có bóng tương phản chống chìm nền mà GPU vẫn 0%, bóng cứng 0px là giải pháp cân bằng hoàn hảo." FontSize="11.5" Foreground="#A0FFA0" TextWrapping="Wrap" Margin="0,2"/>
                    </StackPanel>
                </Border>

            </StackPanel>
        </ScrollViewer>

        <!-- Footer -->
        <Grid Grid.Row="2" Margin="0,16,0,0">
            <TextBlock Text="Ghi chú: Bạn có thể bấm nút X hoặc 'Đóng' bất cứ lúc nào." VerticalAlignment="Center" FontSize="11" Foreground="#888888"/>
            <Button x:Name="BtnClose" Content="Đóng cửa sổ" HorizontalAlignment="Right" Padding="18,8"
                    Background="#333333" Foreground="White" BorderThickness="0"
                    Cursor="Hand" FontWeight="SemiBold" FontSize="11.5"/>
        </Grid>
    </Grid>
</Window>
'@

$reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($xaml))
$window = [System.Windows.Markup.XamlReader]::Load($reader)

$btnClose = $window.FindName("BtnClose")
$btnClose.Add_Click({ $window.Close() })

$btnToggleAnim = $window.FindName("BtnToggleAnim")
$transShadow = $window.FindName("TransShadow")
$transCrisp = $window.FindName("TransCrisp")
$transHard = $window.FindName("TransHard")

$isAnimating = $false
$btnToggleAnim.Add_Click({
    if (-not $isAnimating) {
        $anim = [System.Windows.Media.Animation.DoubleAnimation]::new(0, -200, [System.TimeSpan]::FromSeconds(4))
        $anim.RepeatBehavior = [System.Windows.Media.Animation.RepeatBehavior]::Forever
        $anim.AutoReverse = $true

        $transShadow.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $anim)
        $transCrisp.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $anim)
        $transHard.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $anim)

        $btnToggleAnim.Content = "⏹ Dừng cuộn chữ"
        $btnToggleAnim.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#4A2E2E")
        $btnToggleAnim.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#FF8A80")
        $isAnimating = $true
    } else {
        $transShadow.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $null)
        $transCrisp.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $null)
        $transHard.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $null)

        $transShadow.X = 0
        $transCrisp.X = 0
        $transHard.X = 0

        $btnToggleAnim.Content = "▶ Chạy thử cuộn chữ (Animation)"
        $btnToggleAnim.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#2E4A38")
        $btnToggleAnim.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#1ED760")
        $isAnimating = $false
    }
})

$window.ShowDialog() | Out-Null
