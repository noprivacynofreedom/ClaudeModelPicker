namespace ClaudeModelPicker.Models
{
    public class ModelPick
    {
        public string PickedModel { get; set; } = "Haiku";
        public double Confidence { get; set; }
        public int TokenCount { get; set; }
        public string Reasoning { get; set; } = "";
    }
}
