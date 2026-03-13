using Sres.Net.EEIP;
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;

namespace EIP2042_Controller
{
    /// <summary>
    /// EIP-2042 (16채널 DO 모듈) 통신 커넥션 및 제어 매니저 클래스입니다.
    /// 
    /// [필수 패키지]
    /// NuGet: 'Sres.Net.EEIP' (EEIP) 설치 필요
    /// 
    /// [사용 예시]
    /// // 인스턴스 생성 및 IP 설정
    /// var eipManager = new EIP2042Manager();
    /// eipManager.IpAddress = "192.168.0.10";
    /// 
    /// // 1. 모듈 연결 시도 (UI 프리징 방지를 위해 비동기 권장)
    /// try {
    ///     await eipManager.ConnectAsync();
    /// } catch (Exception ex) {
    ///     // 여기서 "장비의 응답이 없습니다" 등의 경고 메시지를 받아 UI에 표시할 수 있습니다.
    ///     MessageBox.Show(ex.Message); 
    /// }
    /// 
    /// // 2. 채널 출력 제어 (인덱스: 0~15)
    /// if (eipManager.IsConnected) {
    ///     eipManager.SetChannel(0, true); // 0번 ON
    /// }
    /// 
    /// // 3. 연결 해제
    /// eipManager.Disconnect();
    /// 
    /// ※ INotifyPropertyChanged 지원: IsConnected, IpAddress 속성을 UI에 바로 바인딩 가능합니다.
    /// </summary>
    public class EIP2042Manager : INotifyPropertyChanged, IDisposable
    {
        private EEIPClient eeipClient;
        private string ipAddress = "192.168.0.10";
        private bool isConnected;

        // 상태 및 제어 버퍼
        private bool[] doStates = new bool[16];        // 명령 상태
        private bool[] readbackStates = new bool[16];  // 실제 장비 상태 (Readback)

        // 스레드 안정성 및 자율 주행용 멤버
        private readonly object connectionLock = new object();
        private CancellationTokenSource _loopCts;
        private Task _monitoringTask;

        // 설정 및 이벤트
        public bool AutoReconnect { get; set; } = true;
        public int PollIntervalMS { get; set; } = 100;
        public int ReconnectIntervalMS { get; set; } = 5000;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<bool[]> OnReadbackUpdated; // 상태 변경 통지 이벤트

        public string IpAddress
        {
            get => ipAddress;
            set
            {
                ipAddress = value;
                OnPropertyChanged(nameof(IpAddress));
            }
        }

        public bool IsConnected
        {
            get => isConnected;
            private set
            {
                if (isConnected != value)
                {
                    isConnected = value;
                    OnPropertyChanged(nameof(IsConnected));
                }
            }
        }

        public EIP2042Manager()
        {
            eeipClient = new EEIPClient();
        }

        /// <summary>
        /// 비동기로 연결을 시작하고 자율 모니터링 루프를 실행함.
        /// </summary>
        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            // 기존 루프가 있다면 정지
            StopMonitoring();

            await Task.Run(() => Connect());

            if (IsConnected)
            {
                StartMonitoring();
            }
        }

        public void Connect()
        {
            lock (connectionLock)
            {
                if (isConnected) return;

                try
                {
                    // 사전 체크 (타임아웃 1초)
                    if (!PingHost(IpAddress, 44818, 1000))
                        throw new Exception("장비 응답 없음 (Ping/Port 44818 Failure)");

                    eeipClient.IPAddress = IpAddress;
                    eeipClient.RegisterSession();

                    // Implicit Messaging (I/O) 설정
                    eeipClient.O_T_InstanceID = 102;
                    eeipClient.O_T_Length = 2;
                    eeipClient.T_O_InstanceID = 101;
                    eeipClient.T_O_Length = 2;
                    eeipClient.ConfigurationAssemblyInstanceID = 100;

                    eeipClient.O_T_RealTimeFormat = Sres.Net.EEIP.RealTimeFormat.Header32Bit;
                    eeipClient.T_O_RealTimeFormat = Sres.Net.EEIP.RealTimeFormat.Modeless;
                    eeipClient.RequestedPacketRate_O_T = 50000; // 50ms
                    eeipClient.RequestedPacketRate_T_O = 50000; // 50ms

                    // 연결 전 상태 선동기화
                    PreSyncOutputBuffer();

                    eeipClient.ForwardOpen();
                    IsConnected = true;

                    // 내부 상태 배열 초기 동기화
                    SyncStatusWithDevice();
                }
                catch (Exception ex)
                {
                    IsConnected = false;
                    throw new Exception($"연결 실패: {ex.Message}");
                }
            }
        }

        private void StartMonitoring()
        {
            _loopCts = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoop(_loopCts.Token));
        }

        private void StopMonitoring()
        {
            _loopCts?.Cancel();
            _monitoringTask?.Wait(1000); // 종료 대기
            _loopCts?.Dispose();
            _loopCts = null;
        }

        /// <summary>
        /// 내부 자율 루프: 상태 조회 및 자동 재연결을 수행함.
        /// </summary>
        private async Task MonitoringLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (IsConnected)
                {
                    try
                    {
                        // Explicit Message 대신 Implicit Buffer에서 안전하게 읽어옴 (UI 프리징 방지 핵심)
                        byte[] data = eeipClient.T_O_IOData;
                        if (data != null && data.Length >= 2)
                        {
                            bool changed = false;
                            for (int i = 0; i < 16; i++)
                            {
                                int byteIdx = i / 8;
                                int bitIdx = i % 8;
                                bool bitValue = (data[byteIdx] & (1 << bitIdx)) != 0;
                                
                                if (readbackStates[i] != bitValue)
                                {
                                    readbackStates[i] = bitValue;
                                    changed = true;
                                }
                            }

                            if (changed)
                                OnReadbackUpdated?.Invoke((bool[])readbackStates.Clone());
                        }
                    }
                    catch
                    {
                        IsConnected = false; // 통신 에러 감지 시
                    }
                }
                else if (AutoReconnect)
                {
                    // 자동 재연결 시도
                    try { await Task.Run(() => Connect()); } catch { }
                    if (!IsConnected) await Task.Delay(ReconnectIntervalMS, token);
                    continue;
                }

                await Task.Delay(PollIntervalMS, token);
            }
        }

        public void Disconnect()
        {
            StopMonitoring();
            lock (connectionLock)
            {
                if (!IsConnected) return;
                try
                {
                    eeipClient.ForwardClose();
                    eeipClient.UnRegisterSession();
                }
                catch { }
                finally
                {
                    IsConnected = false;
                }
            }
        }

        public void SetChannel(int channel, bool value)
        {
            if (!IsConnected || channel < 0 || channel > 15) return;

            lock (connectionLock)
            {
                doStates[channel] = value;
                byte[] outputData = new byte[2];
                for (int i = 0; i < 16; i++)
                {
                    if (doStates[i])
                        outputData[i / 8] |= (byte)(1 << (i % 8));
                }

                try
                {
                    // Implicit 버퍼와 Explicit 쓰기를 동시 수행하여 즉각 반영 및 유지 보장
                    eeipClient.O_T_IOData[0] = outputData[0];
                    eeipClient.O_T_IOData[1] = outputData[1];
                    eeipClient.AssemblyObject.setInstance(102, outputData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SetChannel Error: {ex.Message}");
                }
            }
        }

        public bool GetChannelStatus(int channel)
        {
            if (channel < 0 || channel > 15) return false;
            return doStates[channel];
        }

        public bool GetActualChannelStatus(int channel)
        {
            if (channel < 0 || channel > 15) return false;
            return readbackStates[channel]; // 내부 자율 루프에서 갱신된 값을 즉시 반환
        }

        private bool PingHost(string hostUri, int portNumber, int timeoutMSec)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var result = client.BeginConnect(hostUri, portNumber, null, null);
                    return result.AsyncWaitHandle.WaitOne(timeoutMSec);
                }
            }
            catch { return false; }
        }

        private void PreSyncOutputBuffer()
        {
            try
            {
                byte[] data = eeipClient.GetAttributeSingle(0x04, 101, 3);
                if (data != null && data.Length >= 2)
                {
                    eeipClient.O_T_IOData[0] = data[0];
                    eeipClient.O_T_IOData[1] = data[1];
                }
            }
            catch { }
        }

        private void SyncStatusWithDevice()
        {
            try
            {
                byte[] data = eeipClient.GetAttributeSingle(0x04, 101, 3);
                if (data != null && data.Length >= 2)
                {
                    for (int i = 0; i < 16; i++)
                        readbackStates[i] = (data[i / 8] & (1 << (i % 8))) != 0;
                    
                    OnReadbackUpdated?.Invoke((bool[])readbackStates.Clone());
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Disconnect();
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
