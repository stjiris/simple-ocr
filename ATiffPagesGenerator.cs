﻿using System.Reflection;
using System.Drawing.Imaging;
using Tess = Tesseract;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using System.Xml;
using OpenCvSharp;
using OpenCvSharp.Text;
using System.ComponentModel;

namespace Tesseract_UI_Tools
{
    public abstract class ATiffPagesGenerator
    {
        public static string[] FORMATS = new string[] { };

        private EncoderParameters QualityEncoderParameters = new EncoderParameters(1);
        protected string FilePath;
        public ATiffPagesGenerator(string FilePath)
        {
            this.FilePath = FilePath;
        }

        public string TiffPage(int I)
        {
            return $"{I}.tiff";
        }

        public string JpegPage(int I, int Dpi, int Quality)
        {
            return $"{I}.{Dpi}.{Quality}.jpeg";
        }
        public string TsvPage(int I, string Languages)
        {
            return $"{I}.{Languages}.tsv";
        }

        public abstract string[] GenerateTIFFs(string FolderPath, bool Overwrite=false, IProgress<float>? Progress=null, BackgroundWorker? worker=null);
        public string[] GenerateJPEGs(string[] TiffPages, string FolderPath, int Dpi = 100, int Quality = 100, bool Overwrite=false, IProgress<float>? Progress = null, BackgroundWorker? worker = null)
        {
            QualityEncoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, Quality);
            ImageCodecInfo JpegImageCodecInfo = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            
            string[] JpegPages = new string[TiffPages.Length];

            for (int i = 0; i < TiffPages.Length && (worker == null || !worker.CancellationPending); i++)
            {
                if (Progress != null) Progress.Report((float)i / TiffPages.Length);

                string FullName = Path.Combine(FolderPath, JpegPage(i, Dpi, Quality));
                JpegPages[i] = FullName;
                if (File.Exists(FullName) && !Overwrite) continue;

                using (Bitmap Tiff = (Bitmap)Image.FromStream(File.OpenText(TiffPages[i]).BaseStream))
                {
                    float Scale = Dpi / Tiff.HorizontalResolution;
                    using (Bitmap Resize = new Bitmap(Tiff, new System.Drawing.Size((int)(Tiff.Width * Scale), (int)(Tiff.Height * Scale))))
                    {
                        Resize.SetResolution(Dpi, Dpi);
                        Resize.Save(FullName, JpegImageCodecInfo, QualityEncoderParameters);
                    }
                }
            }
            
            return JpegPages;
        }

        public string[] GenerateTsvs(string[] TiffPages, string FolderPath, string[] Languages, bool Overwrite=false, IProgress<float>? Progress = null, BackgroundWorker? worker = null)
        {
            string[] HocrPages = new string[TiffPages.Length];
            for (int i = 0; i < TiffPages.Length && (worker == null || !worker.CancellationPending); i++)
            {
                if (Progress != null) Progress.Report((float)i / TiffPages.Length);

                string FullName = Path.Combine(FolderPath, TsvPage(i, TessdataUtil.LanguagesToString(Languages)));
                HocrPages[i] = FullName;
                if (File.Exists(FullName) && !Overwrite) continue;

                using (ResourcesTracker T = new ResourcesTracker())
                {
                    Mat TiffMat = T.T(Cv2.ImRead(TiffPages[i]));
                    OCRTesseract engine = TessdataUtil.CreateEngine(Languages);
                    OCROutput OutputObj = new OCROutput();
                    string Text;
                    engine.Run(TiffMat, out Text, out OutputObj.Rects, componentTexts: out OutputObj.Components, out OutputObj.Confidences);

                    OutputObj.Save(FullName);
                }
                //File.WriteAllText(FullName, ProcessedPage.GetHOCRText(i, true));
            }

            return HocrPages;
        }

        public void GeneratePDF(string[] Jpegs, string[] Tsvs, string[] OriginalTiffs, string OutputFile, float MinConf = 25, IProgress<float>? Progress = null, BackgroundWorker? worker = null)
        {
            PdfDocument doc = new PdfDocument();
            for (int i = 0; i < Jpegs.Length && (worker == null || !worker.CancellationPending); i++)
            {
                if (Progress != null) Progress.Report((float)i / Jpegs.Length);
                PdfPage Page = doc.AddPage();
                XGraphics g = XGraphics.FromPdfPage(Page);
                using( XImage Jpeg = XImage.FromFile(Jpegs[i]))
                {
                    Page.Width = Jpeg.PixelWidth;
                    Page.Height = Jpeg.PixelHeight;
                    g.DrawImage(Jpeg, 0, 0, Jpeg.PixelWidth, Jpeg.PixelHeight);
                }
                PdfUtil.AddTextLayer(g, Tsvs[i], Jpegs[i], OriginalTiffs[i], MinConf);
            }
            if((worker == null || !worker.CancellationPending))
            {
                doc.Save(OutputFile);
            }
        }
    }

    public class TiffPagesGeneratorProvider{
        public static ATiffPagesGenerator? GetTiffPagesGenerator(string FilePath)
        {
            string ext = Path.GetExtension(FilePath).ToLower().Substring(1); // Remove . (dot)
            Type? Generator = Assembly.GetAssembly(typeof(ATiffPagesGenerator)).GetTypes()
                .Where(mType => mType.IsClass && !mType.IsAbstract && mType.IsSubclassOf(typeof(ATiffPagesGenerator)))
                .FirstOrDefault(mType => ((string[])mType.GetField("FORMATS").GetValue(null)).Any(forms => forms.Equals(ext)));
            if (Generator == null)
            {
                return null;
            }

            
            return (ATiffPagesGenerator)Generator.GetConstructor(new Type[] { typeof(string) }).Invoke(new object[] { FilePath });
        }
    }

    public class OCROutput
    {
        public Rect[] Rects = new Rect[] {};
        public string[] Components = new string[] {};
        public float[] Confidences = new float[] {};

        public void Save(string OutputFile, string Debug = "OCROutput")
        {
            System.Diagnostics.Debug.Assert(Rects.Length == Components.Length && Components.Length == Confidences.Length);
            using( StreamWriter writer = new StreamWriter(OutputFile))
            {
                writer.WriteLine($"Origin\tX1\tY1\tX2\tY2\tConfidence\tText");
                for (int i = 0; i < Rects.Length; i++)
                {
                    writer.WriteLine($"{Debug}\t{Rects[i].TopLeft.X}\t{Rects[i].TopLeft.Y}\t{Rects[i].BottomRight.X}\t{Rects[i].BottomRight.Y}\t{Confidences[i]}\t{Components[i]}");
                }
            }
        }
    }
}
