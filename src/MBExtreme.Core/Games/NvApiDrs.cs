using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace MBExtreme.Core.Games;

/// <summary>
/// Perfis de driver NVIDIA por aplicativo, pela interface pública NVAPI (grupo DRS), que é a mesma que o Painel de Controle da NVIDIA e
/// a NVIDIA App usam. Só lida com inteiros (DWORD). Qualquer falha (sem placa NVIDIA, driver antigo, sem permissão) vira "indisponível"
/// ou um texto de erro; nunca derruba o programa. As estruturas têm versão: se o driver não reconhecer, ele recusa em vez de gravar errado.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NvApiDrs : IDriverProfileStore
{
    // Disposição em memória das estruturas do NVAPI (strings são 2048 UTF-16).
    internal const int StrBytes = 4096;
    internal const int SettingSize = 12320, SettingNameAt = 4, SettingIdAt = 4100, SettingTypeAt = 4104, SettingCurrentAt = 8220;
    internal const int ProfileSize = 4116, ProfileNameAt = 4, ProfileGpuAt = 4100;
    internal const int AppSize = 12296, AppNameAt = 8, AppFriendlyAt = 4104;
    internal static uint Version(int size) => (uint)size | (1u << 16);

    private const int Ok = 0;
    private const string ProfilePrefix = "MB Extreme - ";

    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr QueryInterface(uint id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnInit();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnCreateSession(out IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnSession(IntPtr session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnGetBase(IntPtr session, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnFindApp(IntPtr session, IntPtr name, out IntPtr profile, IntPtr app);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnFindProfile(IntPtr session, IntPtr name, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnCreateProfile(IntPtr session, IntPtr info, out IntPtr profile);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnCreateApp(IntPtr session, IntPtr profile, IntPtr app);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnGetSetting(IntPtr session, IntPtr profile, uint id, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnSetSetting(IntPtr session, IntPtr profile, IntPtr setting);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FnDeleteSetting(IntPtr session, IntPtr profile, uint id);

    private sealed record Api(FnCreateSession CreateSession, FnSession Load, FnSession Save, FnSession Destroy, FnGetBase GetBase, FnFindApp FindApp,
        FnFindProfile FindProfile, FnCreateProfile CreateProfile, FnCreateApp CreateApp, FnGetSetting GetSetting, FnSetSetting SetSetting, FnDeleteSetting DeleteSetting);

    private readonly Lazy<Api?> _api = new(LoadApi);

    public bool Available => _api.Value is not null;

    private static T? Fn<T>(uint id) where T : Delegate
    {
        IntPtr p = QueryInterface(id);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    private static Api? LoadApi()
    {
        try
        {
            var init = Fn<FnInit>(0x0150E828);
            if (init is null || init() != Ok) return null;
            var api = new Api(Fn<FnCreateSession>(0x0694D52E)!, Fn<FnSession>(0x375DBD6B)!, Fn<FnSession>(0xFCBC7E14)!, Fn<FnSession>(0xDAD9CFF8)!,
                Fn<FnGetBase>(0xDA8466A0)!, Fn<FnFindApp>(0xEEE566B2)!, Fn<FnFindProfile>(0x7E4A9A0B)!, Fn<FnCreateProfile>(0xCC176068)!,
                Fn<FnCreateApp>(0x4347A9DE)!, Fn<FnGetSetting>(0x73BF8338)!, Fn<FnSetSetting>(0x577DD202)!, Fn<FnDeleteSetting>(0xE4A26362)!);
            return api.CreateSession is null || api.Load is null || api.Save is null || api.Destroy is null || api.GetBase is null || api.FindApp is null
                || api.FindProfile is null || api.CreateProfile is null || api.CreateApp is null || api.GetSetting is null || api.SetSetting is null || api.DeleteSetting is null
                ? null : api;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or MarshalDirectiveException or SEHException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------ buffers

    internal static void PutString(IntPtr buffer, int offset, string text)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(text.Length > 2047 ? text[..2047] : text);
        Marshal.Copy(bytes, 0, buffer + offset, bytes.Length);
    }

    private static IntPtr NewBuffer(int size)
    {
        IntPtr p = Marshal.AllocHGlobal(size);
        for (int i = 0; i < size; i += 8) Marshal.WriteInt64(p, i, 0);
        for (int i = size - size % 8; i < size; i++) Marshal.WriteByte(p, i, 0);
        return p;
    }

    private static IntPtr NameBuffer(string text)
    {
        IntPtr p = NewBuffer(StrBytes);
        PutString(p, 0, text);
        return p;
    }

    private static IntPtr SettingBuffer(uint id, uint? value)
    {
        IntPtr p = NewBuffer(SettingSize);
        Marshal.WriteInt32(p, 0, (int)Version(SettingSize));
        Marshal.WriteInt32(p, SettingIdAt, (int)id);
        if (value is { } v)
        {
            Marshal.WriteInt32(p, SettingTypeAt, 0); // DWORD
            Marshal.WriteInt32(p, SettingCurrentAt, (int)v);
        }
        return p;
    }

    // ------------------------------------------------------------ sessão

    private T Session<T>(Func<Api, IntPtr, T> work, Func<string, T> fail)
    {
        var api = _api.Value;
        if (api is null) return fail("driver NVIDIA indisponível");
        IntPtr session = IntPtr.Zero;
        try
        {
            int rc = api.CreateSession(out session);
            if (rc != Ok) return fail($"código NVAPI {rc} ao abrir a sessão");
            rc = api.Load(session);
            if (rc != Ok) return fail($"código NVAPI {rc} ao ler os perfis");
            return work(api, session);
        }
        catch (Exception ex) when (ex is SEHException or AccessViolationException or MarshalDirectiveException or InvalidOperationException)
        {
            return fail(ex.Message);
        }
        finally
        {
            if (session != IntPtr.Zero) api.Destroy(session);
        }
    }

    private static bool FindProfileOfApp(Api api, IntPtr session, string exe, out IntPtr profile)
    {
        IntPtr name = NameBuffer(exe), app = NewBuffer(AppSize);
        try
        {
            Marshal.WriteInt32(app, 0, (int)Version(AppSize));
            return api.FindApp(session, name, out profile, app) == Ok;
        }
        finally
        {
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(app);
        }
    }

    private static uint? ReadFrom(Api api, IntPtr session, IntPtr profile, uint id)
    {
        IntPtr setting = SettingBuffer(id, null);
        try
        {
            return api.GetSetting(session, profile, id, setting) == Ok ? (uint)Marshal.ReadInt32(setting, SettingCurrentAt) : null;
        }
        finally { Marshal.FreeHGlobal(setting); }
    }

    // ------------------------------------------------------------ IDriverProfileStore

    public DriverValue? Read(string exeName, uint settingId) => Session<DriverValue?>((api, s) =>
    {
        if (FindProfileOfApp(api, s, exeName, out var profile) && ReadFrom(api, s, profile, settingId) is { } own) return new DriverValue(own, true);
        if (api.GetBase(s, out var baseProfile) == Ok && ReadFrom(api, s, baseProfile, settingId) is { } global) return new DriverValue(global, false);
        return null;
    }, _ => null);

    public string? Write(string exeName, uint settingId, uint value) => Session<string?>((api, s) =>
    {
        if (!FindProfileOfApp(api, s, exeName, out var profile))
        {
            string profileName = ProfilePrefix + exeName;
            IntPtr name = NameBuffer(profileName), info = NewBuffer(ProfileSize), app = NewBuffer(AppSize);
            try
            {
                if (api.FindProfile(s, name, out profile) != Ok)
                {
                    Marshal.WriteInt32(info, 0, (int)Version(ProfileSize));
                    PutString(info, ProfileNameAt, profileName);
                    Marshal.WriteInt32(info, ProfileGpuAt, 1); // GeForce
                    int rc = api.CreateProfile(s, info, out profile);
                    if (rc != Ok) return $"código NVAPI {rc} ao criar o perfil do jogo";
                }
                Marshal.WriteInt32(app, 0, (int)Version(AppSize));
                PutString(app, AppNameAt, exeName);
                PutString(app, AppFriendlyAt, exeName);
                int rcApp = api.CreateApp(s, profile, app);
                if (rcApp != Ok) return $"código NVAPI {rcApp} ao ligar o perfil ao jogo";
            }
            finally
            {
                Marshal.FreeHGlobal(name);
                Marshal.FreeHGlobal(info);
                Marshal.FreeHGlobal(app);
            }
        }

        IntPtr setting = SettingBuffer(settingId, value);
        try
        {
            int rc = api.SetSetting(s, profile, setting);
            if (rc != Ok) return $"código NVAPI {rc} ao gravar a configuração";
        }
        finally { Marshal.FreeHGlobal(setting); }

        int save = api.Save(s);
        return save == Ok ? null : $"código NVAPI {save} ao salvar (execute como administrador)";
    }, msg => msg);

    public string? Remove(string exeName, uint settingId) => Session<string?>((api, s) =>
    {
        if (!FindProfileOfApp(api, s, exeName, out var profile)) return null;
        int rc = api.DeleteSetting(s, profile, settingId);
        if (rc != Ok && ReadFrom(api, s, profile, settingId) is not null) return $"código NVAPI {rc} ao remover a configuração";
        int save = api.Save(s);
        return save == Ok ? null : $"código NVAPI {save} ao salvar (execute como administrador)";
    }, msg => msg);
}
