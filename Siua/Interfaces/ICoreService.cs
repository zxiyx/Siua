using System;
using System.Threading.Tasks;

namespace Siua.Interfaces;

public interface ICoreService : IDisposable
{

    bool IsSessionActive { get; }
    Task<bool> LoadPlaywright(string courseUrl);
    Task<bool> ParsePage();
    void StopLoginHeartbeat();
}
