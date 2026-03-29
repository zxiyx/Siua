using System;
using System.Threading.Tasks;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Online;
using Siua.Interfaces;

namespace Siua.Services;

public class PaddleOcrService
{
    private FullOcrModel _model;
    private PaddleOcrAll _all;
    private readonly ILogService _logService;
    public PaddleOcrService(ILogService logService)
    {
        _logService = logService;
    }
    public async Task DownlLoadModel()
    {
        _logService.AddLog("下载OCR模型中....");
        _model = await OnlineFullModels.ChineseV4.DownloadAsync();
        try
        {
            _all = new PaddleOcrAll(_model, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = false,
                Enable180Classification = false,
            };
        }
        catch (Exception ex)
        {
            _all = new PaddleOcrAll(_model, PaddleDevice.PlatformDefault)
            {
                AllowRotateDetection = false,
                Enable180Classification = false,
            };
        }
        _logService.AddLog("OCR模型已加载");
    }
    public string RunOCR(string imagePath)
    {
        using (Mat src = Cv2.ImRead(imagePath))
        {
            PaddleOcrResult result = _all.Run(src);
            return result.Text;
        }
    }
}