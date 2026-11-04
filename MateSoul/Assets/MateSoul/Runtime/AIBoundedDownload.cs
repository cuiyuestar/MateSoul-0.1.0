using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace MateSoul.Runtime
{
    // Shared byte boundary for model responses and public web content.
    internal sealed class AIBoundedDownload : DownloadHandlerScript
    {
        private readonly MemoryStream buffer = new MemoryStream();
        private readonly int limit;
        public bool TooLarge { get; private set; }
        public string Text => Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);

        public AIBoundedDownload(int limit) : base(new byte[16384]) { this.limit = limit; }
        protected override bool ReceiveData(byte[] data, int length)
        {
            if (length > limit - buffer.Length) { TooLarge = true; return false; }
            if (length > 0) buffer.Write(data, 0, length);
            return true;
        }

        public static async Task SendAsync(UnityWebRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (token.IsCancellationRequested) { request.Abort(); token.ThrowIfCancellationRequested(); }
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
        }
    }
}
