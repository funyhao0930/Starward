using Grpc.Core;
using Microsoft.Extensions.Logging;
using Starward.RPC.GameInstall;
using Starward.Core.Localization;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Starward.RPC.Env;

internal class EnviromentController : Env.EnvBase
{


    private readonly ILogger<EnviromentController> _logger;

    private readonly GameInstallHelper _gameInstallHelper;


    public EnviromentController(ILogger<EnviromentController> logger, GameInstallHelper gameInstallHelper)
    {
        _logger = logger;
        _gameInstallHelper = gameInstallHelper;
    }



    public override Task<RpcServerInfo> GetRpcServerInfo(EmptyMessage request, ServerCallContext context)
    {
        return Task.FromResult(new RpcServerInfo
        {
            ProcessId = Environment.ProcessId,
            ProcessPath = Environment.ProcessPath,
        });
    }



    public override Task<ParentProcessMessage> SetParentProcess(ParentProcessMessage request, ServerCallContext context)
    {
        LifecycleManager.SetParentProcess(request.ProcessId, request.KeepRunningOnExited, request.NoLongerChange);
        var (process, keepRunning, noLongerChange) = LifecycleManager.GetParentProcess();
        return Task.FromResult(new ParentProcessMessage
        {
            ProcessId = process?.Id ?? 0,
            KeepRunningOnExited = keepRunning,
            NoLongerChange = noLongerChange,
        });
    }




    public override Task<EmptyMessage> SetEnviroment(EnviromentMessage request, ServerCallContext context)
    {
        LifecycleManager.SetParentProcess(request.ParentProcessId, request.KeepRunningOnExited);
        _gameInstallHelper.SetRateLimiter(request.DownloadRateLimit);
        SetLanguage(request.Language);
        return Task.FromResult(new EmptyMessage());
    }



    /// <summary>
    /// RPC 是另外一个进程，不读 Starward 的设置，默认跟着 Windows 的显示语言走；
    /// 安装器丢给界面的错误信息（CoreLang）要换成与界面相同的语言
    /// </summary>
    private void SetLanguage(string? language)
    {
        try
        {
            CultureInfo culture = string.IsNullOrWhiteSpace(language) ? CultureInfo.InstalledUICulture : new CultureInfo(language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CoreLang.Culture = culture;
        }
        catch (CultureNotFoundException ex)
        {
            _logger.LogWarning(ex, "Unknown language from Starward: {language}", language);
        }
    }



    public override Task<EmptyMessage> StopRpcServer(EmptyMessage request, ServerCallContext context)
    {
        StopProcess();
        return Task.FromResult(new EmptyMessage());
    }


    private static async void StopProcess()
    {
        await Task.Delay(200);
        Environment.Exit(0);
    }



}
