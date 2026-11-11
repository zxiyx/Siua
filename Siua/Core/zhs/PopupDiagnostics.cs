using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Zhs;

internal static class ZhsPopupDiagnostics
{
    public static async Task<string> SaveAsync(IElementHandle element, CancellationToken token, string? directory = null)
    {
        string html;
        try { html = await element.EvaluateAsync<string>("node => (node.closest('.el-overlay,.el-dialog__wrapper') || node).outerHTML").WaitAsync(token); }
        catch (OperationCanceledException) { throw; }
        catch { return "弹窗 HTML 提取失败"; }
        var name = $"Zhs-popup-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.html.txt";
        foreach (var root in new[] { directory ?? Path.Combine(AppContext.BaseDirectory, "Log"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Siua", "Log") })
        {
            try
            {
                Directory.CreateDirectory(root);
                var path = Path.Combine(root, name);
                await File.WriteAllTextAsync(path, html, new UTF8Encoding(false), token);
                return $"弹窗 HTML 已保存：{path}";
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return "弹窗 HTML 保存失败，日志目录不可写";
    }
}
