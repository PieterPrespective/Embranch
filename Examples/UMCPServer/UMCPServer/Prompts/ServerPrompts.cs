using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UMCPServer.Prompts
{

    [McpServerPromptType]
    public class ServerPrompts
    {
        [McpServerPrompt, Description("Creates a system prompt for using the UMCP Server")]
        public static ChatMessage Summarize([Description("The Unity Project root folder")] string rootFolder) =>
            new(ChatRole.User, $"Please summarize this content into a single sentence: {rootFolder}");
    }
}
