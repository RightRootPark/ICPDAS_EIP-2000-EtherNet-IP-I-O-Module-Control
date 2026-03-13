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

        public MainWindow()
        {
            InitializeComponent();
            
            eipManager = new EIP2042Manager();
            IpTextBox.Text = eipManager.IpAddress;
            
            eipManager.PropertyChanged += EipManager_PropertyChanged;
            eipManager.OnReadbackUpdated += EipManager_OnReadbackUpdated; // 자율 업데이트 이벤트 구독
            
            InitializeChannelButtons();
        }

        private void EipManager_OnReadbackUpdated(bool[] actualStates)
        {
            // 백그라운드 쓰레드에서 오므로 Dispatcher 사용 필수
            Dispatcher.BeginInvoke(new Action(() =>
            {
                for (int i = 0; i < 16; i++)
                {
                    bool state = actualStates[i];
                    channelButtons[i].Background = state ? Brushes.LightGreen : Brushes.LightGray;
                    channelButtons[i].Tag = state;
                }
            }));
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
                    Tag = false
                };

                btn.Click += (s, e) =>
                {
                    if (!eipManager.IsConnected)
                    {
                        MessageBox.Show("EIP-2042와 먼저 연결해주세요.", "연결 안됨", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    bool currentState = (bool)btn.Tag;
                    eipManager.SetChannel(index, !currentState);
                    // 색상 변경은 OnReadbackUpdated에서 처리됨
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
                    }
                    else
                    {
                        StatusLabel.Content = "Disconnected";
                        StatusLabel.Foreground = Brushes.Red;
                        ConnectButton.Content = "Connect";

                        foreach (var btn in channelButtons)
                        {
                            btn.Tag = false;
                            btn.Background = Brushes.LightGray;
                        }
                    }
                });
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (eipManager.IsConnected)
            {
                eipManager.Disconnect();
            }
            else
            {
                try
                {
                    ConnectButton.IsEnabled = false;
                    eipManager.IpAddress = IpTextBox.Text;
                    await eipManager.ConnectAsync();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "연결 에러", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ConnectButton.IsEnabled = true;
                }
            }
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            if (eipManager != null)
            {
                eipManager.Dispose(); // 자원 해제 및 감시 쓰레드 정지
            }
        }
    }
}