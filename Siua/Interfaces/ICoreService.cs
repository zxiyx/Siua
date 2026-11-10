using System;
using System.Threading.Tasks;

namespace Siua.Interfaces;

public interface ICoreService : IDisposable
{

    bool IsSessionActive { get; }
    bool CompletedSuccessfully => false;
    Task SaveLoginStateAsync() => Task.CompletedTask;
    Task<bool> LoadPlaywright(string courseUrl);
    Task<bool> ParsePage();
    void StopLoginHeartbeat();
}
