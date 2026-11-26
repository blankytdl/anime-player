// AnimePlayer - Windows 11 (compact) right-click menu entries.
//
// Windows 11 only shows entries of an IExplorerCommand shell extension in the first right-click menu, and Explorer only loads one for a
// program that has a package identity. tools\shell-ext\build-shell-ext.ps1 builds this DLL, wraps it in a tiny signed "sparse" package
// (AppxManifest.xml, no files of its own: the DLL and mpvnet.exe stay in the player folder) and Register-ContextMenu.ps1 installs it.
//
// Two commands, shown for every video / audio file type listed in the manifest:
//   "Play (all) with AnimePlayer"        ->  mpvnet.exe "a" "b" ...
//   "Add (all) to AnimePlayer playlist"  ->  mpvnet.exe --queue "a" "b" ...      (the same command line the classic-menu entries use)
// Explorer hands the WHOLE selection to Invoke(), so several selected files start the player once.
//
// Single source of truth for the CLSIDs: the two DEFINE_COMMAND lines below (build-shell-ext.ps1 reads them for the manifest).
#include <windows.h>
#include <shobjidl_core.h>
#include <shlwapi.h>
#include <wrl/module.h>
#include <wrl/implements.h>
#include <wrl/client.h>
#include <string>

using namespace Microsoft::WRL;

static HINSTANCE g_module = nullptr;

static std::wstring PlayerDir()
{
    wchar_t buf[2 * MAX_PATH] = {};
    DWORD n = GetModuleFileNameW(g_module, buf, ARRAYSIZE(buf));
    std::wstring p(buf, n);
    size_t pos = p.find_last_of(L"\\/");
    return pos == std::wstring::npos ? std::wstring() : p.substr(0, pos);
}

static DWORD SelectionCount(IShellItemArray* items)
{
    DWORD n = 0;
    if (items) items->GetCount(&n);
    return n;
}

static HRESULT Launch(IShellItemArray* items, bool queue)
{
    if (!items) return S_OK;
    DWORD count = 0;
    HRESULT hr = items->GetCount(&count);
    if (FAILED(hr) || count == 0) return S_OK;

    std::wstring dir = PlayerDir();
    std::wstring exe = dir + L"\\mpvnet.exe";
    std::wstring cmd = L"\"" + exe + L"\"";
    if (queue) cmd += L" --queue";

    for (DWORD i = 0; i < count; i++)
    {
        ComPtr<IShellItem> item;
        if (FAILED(items->GetItemAt(i, &item))) continue;
        PWSTR path = nullptr;
        if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)) && path)
        {
            if (cmd.size() + wcslen(path) + 4 < 32000)        // CreateProcess limit is 32767 characters
            {
                cmd += L" \"";
                cmd += path;
                cmd += L"\"";
            }
            CoTaskMemFree(path);
        }
    }

    STARTUPINFOW si = {};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi = {};
    std::wstring mutableCmd = cmd;                             // CreateProcessW may modify the buffer
    if (!CreateProcessW(exe.c_str(), &mutableCmd[0], nullptr, nullptr, FALSE, 0, nullptr, dir.c_str(), &si, &pi))
        return HRESULT_FROM_WIN32(GetLastError());
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return S_OK;
}

#define DEFINE_COMMAND(NAME, CLSID_TEXT, QUEUE, TITLE_ONE, TITLE_MANY)                                                              \
class __declspec(uuid(CLSID_TEXT)) NAME final : public RuntimeClass<RuntimeClassFlags<ClassicCom>, IExplorerCommand, IObjectWithSite> \
{                                                                                                                                   \
public:                                                                                                                             \
    IFACEMETHODIMP GetTitle(IShellItemArray* items, PWSTR* name) override                                                           \
    {                                                                                                                               \
        return SHStrDupW(SelectionCount(items) > 1 ? TITLE_MANY : TITLE_ONE, name);                                                 \
    }                                                                                                                               \
    IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) override                                                                  \
    {                                                                                                                               \
        std::wstring p = PlayerDir() + L"\\mpvnet.exe,0";                                                                           \
        return SHStrDupW(p.c_str(), icon);                                                                                          \
    }                                                                                                                               \
    IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tip) override { *tip = nullptr; return E_NOTIMPL; }                          \
    IFACEMETHODIMP GetCanonicalName(GUID* guid) override { *guid = GUID_NULL; return S_OK; }                                        \
    IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override                                              \
    {                                                                                                                               \
        *state = SelectionCount(items) > 0 ? ECS_ENABLED : ECS_HIDDEN;                                                              \
        return S_OK;                                                                                                                \
    }                                                                                                                               \
    IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override { return Launch(items, QUEUE); }                              \
    IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { *flags = ECF_DEFAULT; return S_OK; }                                     \
    IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** e) override { *e = nullptr; return E_NOTIMPL; }                           \
    IFACEMETHODIMP SetSite(IUnknown* site) override { m_site = site; return S_OK; }                                                 \
    IFACEMETHODIMP GetSite(REFIID riid, void** site) override { return m_site ? m_site.CopyTo(riid, site) : E_FAIL; }               \
private:                                                                                                                            \
    ComPtr<IUnknown> m_site;                                                                                                        \
};

DEFINE_COMMAND(PlayAllCommand, "032AD9C8-D1DE-4F3B-96DA-918206D020DB", false, L"Play with AnimePlayer", L"Play all with AnimePlayer")
DEFINE_COMMAND(AddToPlaylistCommand, "D49C6392-FBF9-4835-AEBE-B10071B081DA", true, L"Add to AnimePlayer playlist", L"Add all to AnimePlayer playlist")

CoCreatableClass(PlayAllCommand)
CoCreatableClass(AddToPlaylistCommand)
CoCreatableClassWrlCreatorMapInclude(PlayAllCommand)
CoCreatableClassWrlCreatorMapInclude(AddToPlaylistCommand)

STDAPI DllGetActivationFactory(_In_ HSTRING activatableClassId, _COM_Outptr_ IActivationFactory** factory)
{
    return Module<ModuleType::InProc>::GetModule().GetActivationFactory(activatableClassId, factory);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().GetObjectCount() == 0 ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _COM_Outptr_ void** instance)
{
    return Module<InProc>::GetModule().GetClassObject(rclsid, riid, instance);
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_module = module;
        DisableThreadLibraryCalls(module);
    }
    return TRUE;
}
