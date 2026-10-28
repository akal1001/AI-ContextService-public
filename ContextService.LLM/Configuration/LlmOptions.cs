using System;
using System.Collections.Generic;
using System.Text;

namespace ContextService.LLM.Configuration
{
    public class LlmOptions
    {
        public string BaseUrl { get; set; } = "http://localhost:11434";
        public string Model { get; set; } = "llama3.2";
    }
}
