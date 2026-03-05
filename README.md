# EIP-2042 EtherNet/IP DO 제어 프로그램

이 프로젝트는 **ICP DAS EIP-2042** (16채널 Isolated Digital Output) 모듈을 PC에서 제어(制御) 및 모니터링하기 위한 WPF 애플리케이션임.

## 1. 프로젝트 개요
- **목적**: EIP-2042 모듈과의 EtherNet/IP 통신을 통해 실시간으로 DO(Digital Output) 상태를 제어하고, 장비의 실제 출력 상태를 피드백 받아 UI에 반영함.
- **개발 환경**: C# 12.0 / .NET 9.0 / WPF (Windows Desktop).
- **통신 라이브러리**: `Sres.Net.EEIP` (EtherNet/IP Client Library).

## 2. 주요 기능
- **연결 관리**: IP 주소를 입력하여 EIP-2042 모듈과 세션 등록 및 Implicit/Explicit Messaging 연결 가능.
- **채널 제어**: 16개의 DO 채널을 개별적으로 ON/OFF 제어(制御). 
- **실시간 모니터링**: 
    - 100ms 주기의 타이머를 통해 장비의 실제 출력 상태(Readback)를 조회.
    - `0x04` (Assembly Object)의 `Instance 101`을 직접 읽어 UI 버튼 색상(초록/회색)으로 상태값 반영.
- **UI/기능 분리**: 통신 로직이 `EIP2042Manager` 클래스로 독립되어 있어 다른 프로젝트에서도 재사용(再使用) 가능.

## 3. 기술 사양 (EIP-2042 기준)
- **IP 주소**: 기본값 `192.168.0.10` (설정 변경 가능).
- **Assembly Instance ID**:
    - **Input (T->O)**: `101 (0x65)` - Readback 데이터 (2 Byte).
    - **Output (O->T)**: `102 (0x66)` - DO 제어 데이터 (2 Byte).
    - **Configuration**: `100 (0x64)`.
- **통신 방식**: 
    - 기본 I/O 데이터는 Implicit Messaging으로 구성.
    - 상태 갱신의 정확성(正確性)을 위해 `GetAttributeSingle` 및 `setInstance`를 통한 Explicit Messaging 동시 활용.

## 4. 클래스 사용법 (EIP2042Manager)

### 클래스 초기화 및 연결
```csharp
var eipManager = new EIP2042Manager();
eipManager.IpAddress = "192.168.0.10";

// 연결 시도
try {
    eipManager.Connect();
} catch (Exception ex) {
    // 연결 실패 처리
}
```

### 채널 출력 제어
```csharp
// channel: 0~15, state: true(ON) / false(OFF)
eipManager.SetChannel(0, true); 
```

### 실제 상태 읽기 (Readback)
```csharp
// 장비에서 물리적으로 출력되고 있는 실제 상태를 반환
bool isRealOn = eipManager.GetActualChannelStatus(0);
```

## 5. 설치 및 설정 주의사항
- **NuGet 패키지**: 프로젝트에 `Sres.Net.EEIP` 패키지가 반드시 설치되어 있어야 함.
- **로그 저장**: 모든 주요 작업 이력은 `AnGLog` 폴더 내에 `YYYY-MM-DD-hh-mm-ss_주요작업내용.txt` 형식으로 자동 저장됨.
- **방화벽 설정**: EtherNet/IP 통신을 위해 UDP 2222번 및 TCP 44818번 포트가 열려 있어야 함.

---
**개발자**: 박정근 (Antigravity AI Assistant 협업)
**최종 수정일**: 2026-03-05
