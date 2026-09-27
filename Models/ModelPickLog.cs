using System;

namespace ClaudeModelPicker.Models
{
    public class ModelPickLog
    {
        public DateTime Timestamp { get; set; }
        public string Prompt { get; set; } = "";
        public string PickedModel { get; set; } = "";
        public double Confidence { get; set; }
    }
}
