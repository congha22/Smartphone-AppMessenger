using Microsoft.Xna.Framework.Graphics;

namespace SmartphoneAppMessenger
{
    public partial class MessengerAppScreen
    {
        private bool TryGetContactAvatarTexture(string playerName, out Texture2D texture)
        {
            texture = null!;
            string? path = MessageManager.GetPlayerAvatarPath(playerName);
            if (string.IsNullOrEmpty(path))
                return false;

            return TryGetImageTexture(path, out texture);
        }

        internal static void ClearAvatarCache()
        {
            foreach (var texture in avatarImageCache.Values)
            {
                if (texture != null && !texture.IsDisposed)
                {
                    try { texture.Dispose(); } catch { }
                }
            }
            avatarImageCache.Clear();
            avatarFailedImagePaths.Clear();
        }
    }
}
