# EIP-2042 EtherNet/IP DO 제어 프로그램

이 프로젝트는 **ICP DAS EIP-2042** (16채널 Isolated Digital Output) 모듈을 PC에서 제어(制御) 및 모니터링하기 위한 WPF 애플리케이션임.

## 1. 프로젝트 개요
- **목적**: EIP-2042 모듈과의 EtherNet/IP 통신을 통해 실시간으로 DO(Digital Output) 상태를 제어하고, 장비의 실제 출력 상태를 피드백 받아 UI에 반영함.
- **개발 환경**: C# 12.0 / .NET 9.0 / WPF (Windows Desktop).
- **통신 라이브러리**: `Sres.Net.EEIP` (EtherNet/IP Client Library).

## 2. 주요 기능
- **연결 관리 (Async & Timeout)**: 
    - `ConnectAsync` 기능을 통해 연결 시도 중 UI가 멈추는 프리징(Freezing) 현상 완벽 방지.
    - `PingHost` 로직을 도입하여 장비가 없거나 전원이 꺼진 경우 1초 내에 즉시 실패를 감지.
- **상태 동기화 (Pre-Sync)**:
    - 연결 직후(ForwardOpen 전) 장비의 현재 상태를 먼저 읽어와 송신 버퍼를 초기화함.
    - 재연결 시 장비의 출력이 0(All OFF)으로 초기화되는 현상을 근본적으로 차단.
- **실시간 모니터링**: 
    - 100ms 주기의 타이머를 통해 장비의 실제 출력 상태(Readback)를 조회.
    - `0x04` (Assembly Object)의 `Instance 101`을 직접 읽어 UI 버튼 색상(초록/회색)으로 상태값 반영.
- **안정성 강화**:
    - 명령 송신 시 Implicit 버퍼와 Explicit 명령을 동기화하여 통신 경합(Race Condition) 해결.
    - 다중 스레드 환경을 고려한 `lock` 처리 및 소유권 충돌(Ownership Conflict) 재시도 로직 포함.

## 3. 기술 사양 (EIP-2042 기준)
- **IP 주소**: 기본값 `192.168.0.10`.
- **Assembly Instance ID**:
    - **Input (T->O)**: `101 (0x65)` - Readback 데이터 (2 Byte).
    - **Output (O->T)**: `102 (0x66)` - DO 제어 데이터 (2 Byte).
    - **Configuration**: `100 (0x64)`.
- **통신 방식**: 
    - 기본 I/O 데이터는 Implicit Messaging(50ms 주기)으로 구성.
    - 명령의 즉각적인 반영을 위해 Explicit Messaging(`setInstance`) 병행 사용.

## 4. 클래스 사용법 (EIP2042Manager)

### 클래스 초기화 및 비동기 연결
```csharp
var eipManager = new EIP2042Manager();
eipManager.IpAddress = "192.168.0.10";

// 비동기 연결 (권장)
try {
    await eipManager.ConnectAsync();
} catch (Exception ex) {
    // "장비의 응답이 없습니다" 등의 상세 에러 메시지 처리
    MessageBox.Show(ex.Message);
}
```

### 채널 출력 제어
```csharp
// channel: 0~15, state: true(ON) / false(OFF)
if (eipManager.IsConnected) {
    eipManager.SetChannel(0, true); 
}
```

### 실제 상태 읽기 (Readback)
```csharp
// 장비에서 물리적으로 출력되고 있는 실제 상태를 반환
bool isRealOn = eipManager.GetActualChannelStatus(0);
```

## 5. 설치 및 설정 주의사항
- **NuGet 패키지**: 프로젝트에 `Sres.Net.EEIP` 패키지가 반드시 설치되어 있어야 함.
- **로그 및 이력**: 모든 작업 이력은 `AnGLog` 폴더 내에 `YYYY-MM-DD-hh-mm-ss_주요작업내용.txt` 형식으로 자동 기록됨.
- **방화벽 설정**: TCP 44818 (Explicit/Register), UDP 2222 (Implicit) 포트 허용 필수.

---
**개발자**: 박정근 (Antigravity AI Assistant 협업)
**최종 업데이트**: 2026-03-11
