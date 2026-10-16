#include "sharedProtocol.h"
#include "vendor/minhook/include/MinHook.h"
#include <shellapi.h>
#include <atomic>
#include <cwchar>

using RoundedTBPin::SharedConfig;
static std::atomic<SharedConfig*> config{nullptr};
static std::atomic<bool> runtimeActive{false};
static volatile LONG initializing = 0;
static HMODULE moduleReference;
static HMODULE taskbarReference;
static HMODULE viewReference;

using HideFn = void(__fastcall*)(void*);
using SecondaryHideFn = void(__fastcall*)(void*, bool);
using ShouldExpandFn = bool(__fastcall*)(void*, HWND, bool);
static HideFn originalHide;
static SecondaryHideFn originalSecondaryHide;
static ShouldExpandFn originalShouldExpand;

static bool IsActive(SharedConfig* value) {
    return runtimeActive.load(std::memory_order_acquire) && value
        && InterlockedCompareExchange(&value->Enabled, 0, 0) == 1;
}

static void __fastcall HideHook(void* self) {
    auto value = config.load(std::memory_order_acquire);
    if (IsActive(value)) { InterlockedIncrement64(&value->HideBlocked); return; }
    originalHide(self);
}

static void __fastcall SecondaryHideHook(void* self, bool hide) {
    auto value = config.load(std::memory_order_acquire);
    if (IsActive(value)) { InterlockedIncrement64(&value->HideBlocked); return; }
    originalSecondaryHide(self, hide);
}

static bool __fastcall ShouldExpandHook(void* self, HWND bar, bool expanded) {
    auto value = config.load(std::memory_order_acquire);
    if (IsActive(value)) { InterlockedIncrement64(&value->ExpandForced); return true; }
    return originalShouldExpand(self, bar, expanded);
}

static HMODULE ValidatedModule(const wchar_t* name, DWORD stamp, DWORD imageSize, DWORD& error) {
    error = ERROR_REVISION_MISMATCH;
    if (!name[0] || !std::wmemchr(name, L'\0', 64) || std::wcschr(name, L'\\') || std::wcschr(name, L'/')) return nullptr;
    auto handle = GetModuleHandleW(name);
    if (!handle) { error = ERROR_MOD_NOT_FOUND; return nullptr; }
    auto base = reinterpret_cast<const BYTE*>(handle);
    auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0 || dos->e_lfanew > 0x100000) return nullptr;
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64
        || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC
        || nt->FileHeader.TimeDateStamp != stamp || nt->OptionalHeader.SizeOfImage != imageSize) return nullptr;
    return handle;
}

static void* ExecutableRva(HMODULE module, uint64_t rva) {
    if (!module || !rva || rva > MAXDWORD) return nullptr;
    auto base = reinterpret_cast<BYTE*>(module);
    auto dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (rva >= nt->OptionalHeader.SizeOfImage) return nullptr;
    auto section = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; ++i) {
        uint64_t start = section[i].VirtualAddress;
        uint64_t end = start + section[i].Misc.VirtualSize;
        if (rva >= start && rva < end && (section[i].Characteristics & IMAGE_SCN_MEM_EXECUTE)) return base + rva;
    }
    return nullptr;
}

static bool CreateHook(void* target, void* detour, void** original) {
    auto result = MH_CreateHook(target, detour, original);
    // Disabled hooks/trampolines remain allocated for safe repeated prototype
    // runs in this Explorer instance. Their addresses do not change.
    return result == MH_OK || (result == MH_ERROR_ALREADY_CREATED && *original);
}

static void RestoreNativeState(uint32_t state) {
    auto bar = FindWindowW(L"Shell_TrayWnd", nullptr);
    if (!bar) return;
    APPBARDATA data{sizeof(data)};
    data.hWnd = bar;
    data.lParam = state;
    SHAppBarMessage(ABM_SETSTATE, &data);
}

static DWORD WINAPI InitializeAndWatch(void* parameter) {
    const DWORD parentId = static_cast<DWORD>(reinterpret_cast<ULONG_PTR>(parameter));
    wchar_t mappingName[96];
    swprintf_s(mappingName, L"%ls%lu", RoundedTBPin::MappingPrefix, parentId);
    auto mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, mappingName);
    auto shared = mapping ? static_cast<SharedConfig*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(SharedConfig))) : nullptr;
    auto parent = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, parentId);
    bool installed = false;
    DWORD error = ERROR_INVALID_DATA;
    if (shared && parent && WaitForSingleObject(parent, 0) == WAIT_TIMEOUT
        && shared->MagicValue == RoundedTBPin::Magic && shared->ProtocolVersion == RoundedTBPin::Version
        && shared->StructSize == sizeof(SharedConfig) && shared->ParentProcessId == parentId) {
        DWORD taskbarError, viewError;
        auto taskbar = ValidatedModule(shared->TaskbarModule, shared->TaskbarTimeDateStamp, shared->TaskbarSizeOfImage, taskbarError);
        auto view = ValidatedModule(shared->ViewModule, shared->ViewTimeDateStamp, shared->ViewSizeOfImage, viewError);
        auto hide = ExecutableRva(taskbar, shared->HideRva);
        auto secondary = shared->SecondaryAutoHideRva ? ExecutableRva(taskbar, shared->SecondaryAutoHideRva) : nullptr;
        auto shouldExpand = ExecutableRva(view, shared->ShouldExpandRva);
        auto update = shared->UpdateIsExpandedRva ? ExecutableRva(view, shared->UpdateIsExpandedRva) : nullptr;
        if (hide && shouldExpand && (!shared->SecondaryAutoHideRva || secondary)
            && (!shared->UpdateIsExpandedRva || update)) {
            // Keep the modules containing patched instructions alive as long
            // as the retained hook trampolines can be called.
            bool retained = (taskbarReference || GetModuleHandleExW(0, shared->TaskbarModule, &taskbarReference))
                && (viewReference || GetModuleHandleExW(0, shared->ViewModule, &viewReference));
            auto initialization = retained ? MH_Initialize() : MH_ERROR_MODULE_NOT_FOUND;
            if ((initialization == MH_OK || initialization == MH_ERROR_ALREADY_INITIALIZED)
                && CreateHook(hide, reinterpret_cast<void*>(&HideHook), reinterpret_cast<void**>(&originalHide))
                && (!secondary || CreateHook(secondary, reinterpret_cast<void*>(&SecondaryHideHook), reinterpret_cast<void**>(&originalSecondaryHide)))
                && CreateHook(shouldExpand, reinterpret_cast<void*>(&ShouldExpandHook), reinterpret_cast<void**>(&originalShouldExpand))) {
                config.store(shared, std::memory_order_release);
                runtimeActive.store(true, std::memory_order_release);
                if (MH_QueueEnableHook(hide) == MH_OK
                    && (!secondary || MH_QueueEnableHook(secondary) == MH_OK)
                    && MH_QueueEnableHook(shouldExpand) == MH_OK && MH_ApplyQueued() == MH_OK) installed = true;
            }
            error = ERROR_INVALID_FUNCTION;
        } else error = !taskbar ? taskbarError : !view ? viewError : ERROR_REVISION_MISMATCH;
    } else if (!parent) error = GetLastError();

    if (installed) {
        InterlockedExchange(&shared->NativeStatus, RoundedTBPin::Ready);
        while (WaitForSingleObject(parent, 100) == WAIT_TIMEOUT
            && InterlockedCompareExchange(&shared->Enabled, 0, 0) == 1) { }
        runtimeActive.store(false, std::memory_order_release);
        // Restore first so Explorer resumes its normal reservation/visibility
        // behavior. The controller remains responsible for its saved work areas.
        RestoreNativeState(shared->OriginalNativeState);
        auto disabled = MH_DisableHook(MH_ALL_HOOKS);
        if (disabled != MH_OK) shared->ErrorCode = 0xe0000000u | static_cast<DWORD>(disabled);
        InterlockedExchange(&initializing, 0);
        InterlockedExchange(&shared->NativeStatus, disabled == MH_OK ? RoundedTBPin::Stopped : RoundedTBPin::Failed);
    } else {
        runtimeActive.store(false, std::memory_order_release);
        MH_DisableHook(MH_ALL_HOOKS);
        if (shared) { shared->ErrorCode = error; InterlockedExchange(&shared->NativeStatus, RoundedTBPin::Failed); }
        InterlockedExchange(&initializing, 0);
    }
    if (parent) CloseHandle(parent);
    // Deliberately retain the DLL reference, mapping and disabled trampolines
    // until Explorer exits: an in-flight hook must never execute freed code.
    // Prototype memory is small; production unloading needs a separate audit.
    (void)mapping;
    return 0;
}

extern "C" __declspec(dllexport) LRESULT CALLBACK HookProc(int code, WPARAM wParam, LPARAM lParam) {
    if (code == HC_ACTION && lParam) {
        auto message = reinterpret_cast<const CWPSTRUCT*>(lParam);
        static UINT start = RegisterWindowMessageW(RoundedTBPin::StartMessage);
        if (message->message == start && message->wParam && InterlockedCompareExchange(&initializing, 1, 0) == 0) {
            // Acquire a lifetime reference before returning to the hook chain.
            if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                reinterpret_cast<LPCWSTR>(&HookProc), &moduleReference)) {
                InterlockedExchange(&initializing, 0);
                return CallNextHookEx(nullptr, code, wParam, lParam);
            }
            auto worker = CreateThread(nullptr, 0, InitializeAndWatch,
                reinterpret_cast<void*>(message->wParam), 0, nullptr);
            if (worker) CloseHandle(worker);
            else InterlockedExchange(&initializing, 0);
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*) {
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(instance);
    return TRUE;
}
