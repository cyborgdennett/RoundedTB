#pragma once
#include <windows.h>
#include <cstdint>

namespace RoundedTBPin {
constexpr uint32_t Magic = 0x50544252; // RBTP
constexpr uint32_t Version = 1;
constexpr wchar_t MappingPrefix[] = L"Local\\RoundedTB.TaskbarPin.";
constexpr wchar_t StartMessage[] = L"RoundedTB.TaskbarPin.Start.1";
enum Status : LONG { Pending = 0, Ready = 1, Stopped = 2, Failed = 3 };

// x64, packing 8. RVAs must originate from the exact loaded PE/PDB version.
// The controller initializes Enabled=1, waits for Ready, then enables native
// auto-hide. Setting Enabled=0 requests restoration and hook deactivation.
#pragma pack(push, 8)
struct SharedConfig {
    uint32_t MagicValue;
    uint32_t ProtocolVersion;
    uint32_t StructSize;
    uint32_t ParentProcessId;
    uint32_t TaskbarTimeDateStamp;
    uint32_t TaskbarSizeOfImage;
    uint32_t ViewTimeDateStamp;
    uint32_t ViewSizeOfImage;
    uint64_t HideRva;
    uint64_t SecondaryAutoHideRva; // zero is allowed for primary-only prototype
    uint64_t ShouldExpandRva;
    uint64_t UpdateIsExpandedRva; // validated when nonzero; not invoked yet
    uint32_t OriginalNativeState;
    volatile LONG Enabled;
    volatile LONG NativeStatus;
    uint32_t ErrorCode;
    volatile LONG64 HideBlocked;
    volatile LONG64 ExpandForced;
    wchar_t TaskbarModule[64];
    wchar_t ViewModule[64];
};
#pragma pack(pop)
static_assert(sizeof(SharedConfig) == 352);
}
