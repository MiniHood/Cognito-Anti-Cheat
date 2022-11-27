    public class CallDetection
    {
        public string DetectPath(string imagepath)
        {
            byte[] imageBytes = File.ReadAllBytes(imagepath);
            Detection.ModelInput sampleData = new Detection.ModelInput()
            {
                ImageSource = imageBytes,
            };

            var predictionResult = Detection.Predict(sampleData);

            return String.Join(",", predictionResult.Score);
        }

        public string DetectImage(byte[] imageBytes)
        {
            Detection.ModelInput sampleData = new Detection.ModelInput()
            {
                ImageSource = imageBytes,
            };

            var predictionResult = Detection.Predict(sampleData);

            return $"\n\nPredicted Label value: {predictionResult.PredictedLabel} \nPredicted Label scores: [{String.Join(",", predictionResult.Score)}]\n\n";
        }

        private Bitmap TakePhoto()
        {
            Size shotSize = Screen.PrimaryScreen.Bounds.Size;
            Point upperScreenPoint = new Point(0, 0);
            Point upperDestinationPoint = new Point(0, 0);
            Bitmap shot = new Bitmap(shotSize.Width, shotSize.Height);
            Graphics graphics = Graphics.FromImage(shot);
            graphics.CopyFromScreen(upperScreenPoint, upperDestinationPoint, shotSize);
            return shot;
        }

        private static byte[] ImageToByte(Image img)
        {
            using (var stream = new MemoryStream())
            {
                img.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }

        public string ScreenshotDetection()
        {
            byte[] b = ImageToByte(TakePhoto());

            return DetectImage(b);
        }
    }
