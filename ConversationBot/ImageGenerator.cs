using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConversationBot
{
    public class ImageGenerator
    {
        private class Rootobject
        {
            public int fn_index { get; set; }
            public List<object> data { get; set; } = default!;
            public string session_hash { get; set; } = default!;
        }

        private class PredictionResult
        {
            public List<object> data { get; set; } = default!;
            public bool is_generating { get; set; }
            public float duration { get; set; }
            public float average_duration { get; set; }
        }

        public class PromptResultData
        {
            public class PredictionResultInfo
            {
                public class Extra_Generation_Params
                {
                }

                public string prompt { get; set; } = default!;
                public string[] all_prompts { get; set; } = default!;
                public string negative_prompt { get; set; } = default!;
                public long seed { get; set; }
                public long[] all_seeds { get; set; } = default!;
                public long subseed { get; set; }
                public long[] all_subseeds { get; set; } = default!;
                public int subseed_strength { get; set; }
                public int width { get; set; }
                public int height { get; set; }
                public int sampler_index { get; set; }
                public string sampler { get; set; } = default!;
                public int cfg_scale { get; set; }
                public int steps { get; set; }
                public int batch_size { get; set; }
                public bool restore_faces { get; set; }
                public object face_restoration_model { get; set; } = default!;
                public string sd_model_hash { get; set; } = default!;
                public int seed_resize_from_w { get; set; }
                public int seed_resize_from_h { get; set; }
                public object denoising_strength { get; set; } = default!;
                public Extra_Generation_Params extra_generation_params { get; set; } = default!;
                public int index_of_first_image { get; set; }
                public string[] infotexts { get; set; } = default!;
                public string[] styles { get; set; } = default!;
                public string job_timestamp { get; set; } = default!;
            }


            public PredictionResultInfo ResultInfo { get; set; } = default!;
            public string Prompt { get; set; } = default!;
            public string SafeName { get; set; } = default!;
            public byte[] ImageBytes { get; set; } = default!;
            public string? ImagePathOnDisk { get; set; }
        }

        private class ImageResultData
        {
            public string name { get; set; } = default!;
            public object data { get; set; } = default!;
            public bool is_file { get; set; }
        }

        public static async Task<PromptResultData> GetImage(string prompt, bool isTiled = false, int size = 512)
        {
            Rootobject requestData = new Rootobject()
            {
                fn_index = 13,
                session_hash = "y674hs44uh9",
                data = new List<object>()
                {
                    prompt,
                    "",
                    "None",
                    "None",
                    size > 512 ? 30 : 35,
                    "Euler a",
                    false,
                    isTiled,
                    1,
                    1,
                    11,
                    -1,
                    -1,
                    0,
                    0,
                    0,
                    false,
                    size,
                    size,
                    false,
                    0.8,
                    0,
                    0,
                    "None",
                    false,
                    false,
                    null!,
                    "",
                    "Seed",
                    "",
                    "Nothing",
                    "",
                    true,
                    false,
                    false,
                    null!,
                    "",
                    "",
                }
            };

            using (var client = new HttpClient())
            {
                var result = await client.PostAsJsonAsync("http://127.0.0.1:7860/api/predict", requestData);
                var resultString = await result.Content.ReadAsStringAsync();

                var jsonResult = System.Text.Json.JsonSerializer.Deserialize<PredictionResult>(resultString) ?? throw new Exception("Failed to deserialize result string");
                var data = (System.Text.Json.JsonElement)jsonResult.data[0];

                // TODO: What even is this
                var imgDataArray = data.Deserialize<List<object>>();

                var jsonResult2 = System.Text.Json.JsonSerializer.Deserialize<ImageResultData>(imgDataArray![0].ToString()!) ?? throw new Exception();

                byte[]? imageBytes = null;
                string? imagePathOnDisk = null;
                if (jsonResult2.is_file && jsonResult2.name.StartsWith(@"C:\Users\Nop\AppData\Local\Temp\"))
                {
                    imagePathOnDisk = jsonResult2.name;

                    for (var i = 0; i < 10; i++)
                    {
                        try
                        {
                            using (var fs = new FileStream(jsonResult2.name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                using (var br = new BinaryReader(fs))
                                {
                                    imageBytes = br.ReadBytes((int)br.BaseStream.Length);
                                }
                            }
                            break;
                        }
                        catch (Exception)
                        {
                            Thread.Sleep(20);
                        }
                    }

                }
                else
                {
                    var imgDataB4 = imgDataArray[0].ToString() ?? throw new Exception();
                    var imgDataB4_2 = imgDataB4.Substring(imgDataB4.IndexOf(',') + 1);
                    imageBytes = Convert.FromBase64String(imgDataB4_2);
                }

                var data2 = ((System.Text.Json.JsonElement)jsonResult.data[1]).ToString();
                var predictionResult = JsonSerializer.Deserialize<PromptResultData.PredictionResultInfo>(data2) ?? throw new Exception("Failed to deserialize data2");

                var filename = prompt.Trim().Substring(0, Math.Min(64, prompt.Length)).Replace(" ", "-");
                filename = Regex.Replace(filename, @"[^a-zA-Z0-9\-]", string.Empty);

                if (imageBytes == null)
                {
                    return new PromptResultData()
                    {
                        ImageBytes = new byte[] { },
                        SafeName = $"{predictionResult.seed}_{filename}",
                        Prompt = prompt,
                        ImagePathOnDisk = imagePathOnDisk,
                        ResultInfo = predictionResult
                    };
                }

                return new PromptResultData()
                {
                    ImageBytes = imageBytes,
                    SafeName = $"{predictionResult.seed}_{filename}",
                    Prompt = prompt,
                    ImagePathOnDisk = imagePathOnDisk,
                    ResultInfo = predictionResult
                };
            }
        }
    }
}
