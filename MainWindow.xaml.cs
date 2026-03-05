using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EIP2042_Controller
{
    public partial class MainWindow : Window
    {
        private EIP2042Manager eipManager;
        private Button[] channelButtons = new Button[16];
        private DispatcherTimer updateTimer;

        public MainWindow()
        {
            InitializeComponent();
            
            eipManager = new EIP2042Manager();
            IpTextBox.Text = eipManager.IpAddress;
            
            eipManager.PropertyChanged += EipManager_PropertyChanged;
            
            InitializeChannelButtons();

            // 백그라운드 Readback 상태 감시 타이머 (100ms마다 확인)
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromMilliseconds(100);
            updateTimer.Tick += UpdateTimer_Tick;
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!eipManager.IsConnected) return;

            for (int i = 0; i < 16; i++)
            {
                // 실제 기기의 수신(Readback) 상태를 1채널 비트 단위로 가져옴
                bool actualState = eipManager.GetActualChannelStatus(i);
                
                // UI 버튼 색상 업데이트 (Tag를 저장하는 대신 실제 Readback을 바로 반영)
                channelButtons[i].Background = actualState ? Brushes.LightGreen : Brushes.LightGray;
                channelButtons[i].Tag = actualState; // 현재 클릭 토글을 위해 편의상 Tag 업데이트
            }
        }

        private void InitializeChannelButtons()
        {
            for (int i = 0; i < 16; i++)
            {
                int index = i;
                Button btn = new Button
                {
                    Content = $"CH {i:00}",
                    Width = 60,
                    Height = 60,
                    Margin = new Thickness(5),
                    Background = Brushes.LightGray,
                    Tag = false // 저장 상태: false = OFF, true = ON
                };

                btn.Click += (s, e) =>
                {
                    if (!eipManager.IsConnected)
                    {
                        MessageBox.Show("EIP-2042와 먼저 연결해주세요.", "연결 안됨", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    bool currentState = (bool)btn.Tag;
                    bool newState = !currentState;
                    
                    try
                    {
                        eipManager.SetChannel(index, newState);
                        
                        // 클릭 시 색상을 바로 변경하지 않음. 타이머가 실제 Readback 상태를 가져와 녹색으로 바꿔줄 거임
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"채널 {index} 상태 변경 실패: {ex.Message}", "에러", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                channelButtons[i] = btn;
                ChannelsPanel.Children.Add(btn);
            }
        }

        private void EipManager_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(EIP2042Manager.IsConnected))
            {
                Dispatcher.Invoke(() =>
                {
                    if (eipManager.IsConnected)
                    {
                        StatusLabel.Content = "Connected";
                        StatusLabel.Foreground = Brushes.Green;
                        ConnectButton.Content = "Disconnect";
                        updateTimer.Start(); // 연결 성공 후 상태 감시 시작
                    }
                    else
                    {
                        StatusLabel.Content = "Disconnected";
                        StatusLabel.Foreground = Brushes.Red;
                        ConnectButton.Content = "Connect";
                        updateTimer.Stop(); // 연결 끊어지면 타이머 중지

                        // 연결 해제시 상태 초기화
                        foreach (var btn in channelButtons)
                        {
                            btn.Tag = false;
                            btn.Background = Brushes.LightGray;
                        }
                    }
                });
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (eipManager.IsConnected)
            {
                eipManager.Disconnect();
            }
            else
            {
                try
                {
                    eipManager.IpAddress = IpTextBox.Text;
                    eipManager.Connect();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "연결 에러", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (eipManager != null && eipManager.IsConnected)
            {
                eipManager.Disconnect();
            }
        }
    }
}