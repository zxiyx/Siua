using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Siua.Core;

namespace Siua.Core.Xxt;

public sealed partial class XxtQuestion
{
    private string[]? _randomText;
    private int[]? _randomSelection;
    private string[] _randomOptionKeys = [];

    public async Task PrepareRandomAnswerAsync(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        string? previous = null;
        while (timer.ElapsedMilliseconds < 10_000)
        {
            await LoadAnswersAsync(cancellationToken);
            if ((IsFillInBlank || IsTextAnswer) && _blanks.Count > 0)
            {
                _randomText = RandomAnswerSelector.FillBlanks(_blanks.Count).ToArray();
                return;
            }
            var signature = string.Join("|", _answers.Select(answer => answer.Marker + ":" + answer.Text));
            if (_answers.Count > 0 && signature == previous)
            {
                _randomSelection = RandomAnswerSelector.Select(_answers.Count, AllowsMultipleAnswers).ToArray();
                _randomOptionKeys = await ReadOptionKeysAsync();
                return;
            }
            previous = signature;
            await Task.Delay(300, cancellationToken);
        }
        throw new PlaywrightException("题目答题控件未就绪或题型不受支持，未生成随机答案。");
    }

    // 初次填写及提交前检查使用同一份答案；最多两次重试，只修补状态不一致的控件。
    public async Task EnsureRandomAnswerAsync(CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (_randomText is not null)
                    await FillInputsAsync(_randomText, cancellationToken, onlyMismatches: true);
                else if (_randomSelection is { Length: > 0 })
                {
                    if (!(await ReadOptionKeysAsync()).SequenceEqual(_randomOptionKeys))
                        throw new PlaywrightException("本题选项数量、顺序或内容发生变化，已停止填写。");
                    for (var index = 0; index < _answers.Count; index++)
                    {
                        var selected = await ReadSelectedAsync(_answers[index]);
                        if (selected is null)
                            throw new PlaywrightException($"选项 {_answers[index].Marker} 无法读取选中状态。");
                        var expected = _randomSelection.Contains(index);
                        // 单选选中目标即可自动清除其它项；多选只切换不匹配的项。
                        if (selected != expected && (expected || AllowsMultipleAnswers))
                        {
                            await _answers[index].Target.ClickAsync(new LocatorClickOptions { Timeout = 2000 })
                                .WaitAsync(cancellationToken);
                            await Task.Delay(250, cancellationToken);
                        }
                    }
                }
                else throw new PlaywrightException("本题没有已生成的随机答案。");

                // 给 change/blur 回调及异步页面状态留出更新时间。
                await Task.Delay(400, cancellationToken);
                if (await RandomAnswerMatchesAsync()) return;
                lastError = new PlaywrightException("填写后的选中状态或文本与预期答案不一致。");
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lastError = exception;
            }
            if (attempt < 2) await Task.Delay(400, cancellationToken);
        }
        throw new PlaywrightException($"随机答案填写或复核失败（已重试两次）：{lastError}");
    }

    private async Task<bool> RandomAnswerMatchesAsync()
    {
        if (_randomText is not null)
        {
            for (var index = 0; index < _blanks.Count; index++)
                if (!await BlankMatchesAsync(_blanks[index], _randomText[index])) return false;
            return true;
        }
        if (_randomSelection is null) return false;
        if (!(await ReadOptionKeysAsync()).SequenceEqual(_randomOptionKeys)) return false;
        for (var index = 0; index < _answers.Count; index++)
            if (await ReadSelectedAsync(_answers[index]) != _randomSelection.Contains(index)) return false;
        return true;
    }

    private Task<string[]> ReadOptionKeysAsync() => _container.EvaluateAsync<string[]>("""
        node => [...node.querySelectorAll('li')]
            .filter(item => item.querySelector('label') && item.querySelector('a'))
            .map(item => JSON.stringify([item.querySelector('label').textContent,
                item.querySelector('a').textContent, item.querySelector('.num_option')?.getAttribute('data')]))
        """);

    public async Task VerifyRandomAnswerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await RandomAnswerMatchesAsync())
            throw new PlaywrightException("随机答案在补填其它题目后再次发生变化，已停止提交。");
    }

    private static Task<bool?> ReadSelectedAsync(XxtAnswerOption answer) => answer.Target.EvaluateAsync<bool?>("""
        (label, markerText) => {
            const item = label.closest('li') || label;
            const input = label.control || item.querySelector('input[type=radio], input[type=checkbox]');
            if (input) return input.checked;
            const marker = item.querySelector('.num_option');
            const name = marker?.getAttribute('name') || (item.getAttribute('qid') ? 'answer' + item.getAttribute('qid') : null);
            const field = name && label.ownerDocument.getElementById(name);
            if (field && 'value' in field) {
                const value = field.value.trim().toUpperCase();
                const key = (marker?.getAttribute('data') || markerText).trim().toUpperCase();
                if (!value) return false;
                if (value === key) return true;
                if (/^[A-H]+$/.test(value) && /^[A-H]$/.test(key)) return value.includes(key);
                return value.split(/[,;|\s]+/).includes(key);
            }
            const aria = item.getAttribute('aria-checked') ?? label.getAttribute('aria-checked');
            if (aria === 'true' || aria === 'false') return aria === 'true';
            return null;
        }
        """, answer.Marker);

    private static Task<bool> BlankMatchesAsync(BlankInput blank, string answer) => blank.Input.EvaluateAsync<bool>("""
        (element, args) => {
            const normalize = value => (value || '').replace(/\s+/g, ' ').trim();
            const text = html => {
                const node = element.ownerDocument.createElement('div');
                node.innerHTML = html;
                for (const child of node.querySelectorAll('br,p,div,li')) {
                    if (child.tagName !== 'BR') child.before('\n');
                    child.after('\n');
                }
                return normalize(node.textContent);
            };
            const expected = normalize(args.answer);
            if (!element.isContentEditable) return normalize(element.value) === expected;
            if (text(element.innerHTML) !== expected) return false;
            if (!args.requiresEditor) return true;
            const view = element.ownerDocument.defaultView;
            const views = [view];
            try { if (view.parent !== view && view.parent.document) views.push(view.parent); } catch {}
            const editor = views.flatMap(owner => Object.values(owner.UE?.instants || {}))
                .find(item => item.body === element || item.body?.contains(element));
            if (!editor) return false;
            const owner = editor.container?.ownerDocument || view.parent.document;
            const form = editor.container?.closest('form') || editor.iframe?.closest('form');
            const field = args.fieldId ? owner.getElementById(args.fieldId)
                : editor.textarea || (editor.key && form?.elements.namedItem(editor.key));
            if (!field || text(field.value) !== expected) return false;
            return !args.mirrorId || text(owner.getElementById(args.mirrorId)?.innerHTML) === expected;
        }
        """, new { answer, requiresEditor = blank.RequiresEditor, fieldId = blank.FieldId, mirrorId = blank.MirrorId });
}
