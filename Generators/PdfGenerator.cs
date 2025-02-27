﻿using System.ComponentModel;
using GroupDocs.Parser;
using GroupDocs.Parser.Options;
using IRIS_OCR_Desktop;

namespace IRIS_OCR_Desktop.Generators
{
    public class PdfGenerator : ATiffPagesGenerator
    {
        public static readonly new string[] FORMATS = new string[] { "pdf" };

        public PdfGenerator(string FilePath) : base(FilePath)
        {
            using Parser PDFParser = new(FilePath);
            CanRun = !PDFParser.GetMetadata().Any(o => o.Name == "application" && o.Value == PDF_TAG);
        }

        private static string BmpFile(int i)
        {
            return $"{i}.bmp";
        }

        public override string[] GenerateTIFFs(string FolderPath, bool Overwrite = false)
        {
            if (!CanRun) throw new Exception("Attempting to run a File already generated by Tesseract UI Tools");
            string[] Pages;
            using Parser PDFParser = new(FilePath);
            int PagesNumber = PDFParser.GetDocumentInfo().PageCount;
            Pages = new string[PagesNumber];
            for (int i = 0; i < PagesNumber && (worker == null || !worker.CancellationPending); i++)
            {
                if (Progress != null) Progress.Report(i / (float)PagesNumber);
                string FullName = Path.Combine(FolderPath, TiffPage(i));
                Pages[i] = FullName;
                if (File.Exists(FullName) && !Overwrite) continue;

                string TmpName = Path.Combine(FolderPath, BmpFile(i));
                PreviewOptions previewOptions = new(
                    _ => File.Create(TmpName),
                    (_, stream) =>
                    {
                        using Bitmap bit = new(stream);
                        bit.Save(FullName);
                        stream.Close();
                        File.Delete(TmpName);
                    })
                {
                    PreviewFormat = PreviewOptions.PreviewFormats.BMP,
                    PageNumbers = new int[] { i + 1 },
                    Dpi = 300
                };

                PDFParser.GeneratePreview(previewOptions);
            }
            return Pages;
        }
    }
}
