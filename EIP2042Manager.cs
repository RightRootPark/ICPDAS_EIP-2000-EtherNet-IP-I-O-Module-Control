using Sres.Net.EEIP;
using System;
using System.ComponentModel;
using System.Threading;

namespace EIP2042_Controller
{
    /// <summary>
    /// EIP-2042 (16채널 DO 모듈) 통신 커넥션 및 제어 매니저 클래스입니다.
    /// 
    /// [필수 패키지]
    /// NuGet: 'Sres.Net.EEIP' (EEIP) 설치 필요
    /// 
    /// [사용 예시]
    /// // 클래스 인스턴스 생성 및 IP 지정
    /// var eipManager = new EIP2042Manager();
    /// eipManager.IpAddress = "192.168.0.10";
    /// 
    /// // 1. 모듈 연결 시도 (에러 발생 시 Exception throw)
    /// eipManager.Connect();
    /// 
    /// // 2. 채널 출력 제어 (인덱스: 0~15, true: ON, false: OFF)
    /// eipManager.SetChannel(0, true); // 0번 채널 ON
    /// bool state = eipManager.GetChannelStatus(0); // 현재 상태 읽기
    /// 
    /// // 3. 모듈 연결 해제
    /// eipManager.Disconnect();
    /// 
    /// ※ INotifyPropertyChanged 인터페이스를 지원하므로 WPF Binding(예: IsConnected, IpAddress)과 연동하기 쉽습니다.
    /// </summary>
    public class EIP2042Manager : INotifyPropertyChanged
    {
        private EEIPClient eeipClient;
        private string ipAddress = "192.168.0.10";
        private bool isConnected;

        // 16채널 DO 상태 추적 배열
        private bool[] doStatus = new bool[16];

        public event PropertyChangedEventHandler PropertyChanged;

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
                isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }

        public EIP2042Manager()
        {
            eeipClient = new EEIPClient();
        }

        public void Connect()
        {
            if (isConnected) return;

            try
            {
                eeipClient.IPAddress = IpAddress;
                eeipClient.RegisterSession();

                // EIP-2042 Implicit Messaging 설정
                eeipClient.O_T_InstanceID = 102; // Output Assembly
                eeipClient.O_T_Length = 2;       // DO0~DO7, DO8~DO15
                eeipClient.T_O_InstanceID = 101; // Input Assembly
                eeipClient.T_O_Length = 2;       // Readback
                eeipClient.ConfigurationAssemblyInstanceID = 100; // Configuration Assembly (매뉴얼 기준 0x64)
                
                eeipClient.O_T_RealTimeFormat = Sres.Net.EEIP.RealTimeFormat.Header32Bit;
                eeipClient.T_O_RealTimeFormat = Sres.Net.EEIP.RealTimeFormat.Modeless;
                eeipClient.RequestedPacketRate_O_T = 50000; // 50ms
                eeipClient.RequestedPacketRate_T_O = 50000; // 50ms

                // 중요: ForwardOpen 전에 장치의 현재 상태를 Explicit Message로 먼저 읽어와서 송신 버퍼에 채워넣음
                // 이렇게 하면 연결되자마자 0(All OFF)이 송신되어 장치가 꺼지는 것을 방지할 수 있음
                PreSyncOutputBuffer();

                eeipClient.ForwardOpen();
                IsConnected = true;

                // 내부 로직용 doStatus 배열 동기화
                SyncStatusWithDevice();
            }
            catch (Exception ex)
            {
                IsConnected = false;
                throw new Exception($"EIP-2042 연결 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 연결 전 장치의 현재 상태를 읽어 송신 버퍼(O_T_IOData)에 미리 써넣습니다.
        /// </summary>
        private void PreSyncOutputBuffer()
        {
            try
            {
                // Instance 101 (Readback)을 읽어옴
                byte[] readbackData = eeipClient.GetAttributeSingle(0x04, 101, 3);
                if (readbackData != null && readbackData.Length >= 2)
                {
                    // Implicit Messaging 버퍼 준비
                    eeipClient.O_T_IOData[0] = readbackData[0];
                    eeipClient.O_T_IOData[1] = readbackData[1];
                }
            }
            catch { /* 연결 전이라 실패할 수 있음 */ }
        }

        /// <summary>
        /// 장비의 실제 출력 상태(Readback)를 읽어와 내부 doStatus 및 O_T_IOData 버퍼를 동기화합니다.
        /// </summary>
        private void SyncStatusWithDevice()
        {
            try
            {
                // 이미 연결된 상태이므로 T_O_IOData 또는 GetAttributeSingle 사용 가능
                byte[] readbackData = eeipClient.GetAttributeSingle(0x04, 101, 3);
                if (readbackData != null && readbackData.Length >= 2)
                {
                    eeipClient.O_T_IOData[0] = readbackData[0];
                    eeipClient.O_T_IOData[1] = readbackData[1];

                    for (int i = 0; i < 16; i++)
                    {
                        int byteIndex = i / 8;
                        int bitIndex = i % 8;
                        doStatus[i] = (readbackData[byteIndex] & (1 << bitIndex)) != 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SyncStatusWithDevice 에러: {ex.Message}");
            }
        }

        public void Disconnect()
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
                // doStatus 초기화 제거: 다시 연결했을 때 이전 상태를 UI나 로직에서 참고할 수도 있으므로 명시적 초기화 안 함
                // 실제 동기화는 Connect 시 SyncStatusWithDevice에서 수행됨
            }
        }

        public void SetChannel(int channel, bool value)
        {
            if (!IsConnected) return;
            if (channel < 0 || channel > 15) return;

            doStatus[channel] = value;

            // 1. 현재 DO 상태 배열을 기반으로 2바이트(16비트) 조합 생성
            byte[] outputData = new byte[2];
            for (int i = 0; i < 16; i++)
            {
                if (doStatus[i])
                {
                    int byteIndex = i / 8;
                    int bitIndex = i % 8;
                    outputData[byteIndex] |= (byte)(1 << bitIndex);
                }
            }

            try
            {
                // 2. Implicit Message용 버퍼(O_T_IOData)도 동기화하여 백그라운드 전송 시 값이 유지되도록 함
                eeipClient.O_T_IOData[0] = outputData[0];
                eeipClient.O_T_IOData[1] = outputData[1];

                // 3. Explicit Message를 통해 Assembly Object (Class 0x04), Instance 102 (0x66) 
                // EIP-2042 Output Assembly Data를 통째로 덮어씁니다.
                eeipClient.AssemblyObject.setInstance(102, outputData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Explicit Message SetChannel Error: {ex.Message}");
            }
        }

        public bool GetChannelStatus(int channel)
        {
            if (channel < 0 || channel > 15) return false;
            return doStatus[channel];
        }

        public bool GetActualChannelStatus(int channel)
        {
            if (!IsConnected || channel < 0 || channel > 15) return false;

            try
            {
                // EIP-2042 Readback 시 Assembly Object (Class 0x04), Instance 101 (0x65), Attribute 3 (Data)를 Explicit Message로 직접 읽어옵니다.
                // Implicit Message의 T_O_IOData 갱신이 원활하지 않은 경우를 대비한 방식입니다.
                byte[] readbackData = eeipClient.GetAttributeSingle(0x04, 101, 3);
                
                if (readbackData != null && readbackData.Length >= 2)
                {
                    int byteIndex = channel / 8;
                    int bitIndex = channel % 8;
                    byte currentData = readbackData[byteIndex];
                    return (currentData & (1 << bitIndex)) != 0;
                }
            }
            catch(Exception ex)
            {
                // 실패 시 로그를 남길 수 있으나, 타이머에서 주기적으로 호출되므로 예외를 삼키고 false 반환
                Console.WriteLine($"Explicit Message GetActualChannelStatus Error: {ex.Message}");
            }

            return false;
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
