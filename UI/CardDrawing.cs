using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace SmartphoneAppMessenger.UI
{
    public static class CardDrawing
    {
        public static void DrawCard(SpriteBatch b, int x, int y, int width, int height, Color color, float scale = 1f, bool drawShadow = false, float draw_layer = -1f)
        {
            var api = ModEntry.iSmartphoneApi;
            Texture2D texture = api?.GetCardTexture() ?? Game1.menuTexture;
            Rectangle sourceRect = api?.GetCardTexture() != null ? texture.Bounds : new Rectangle(0, 256, 60, 60);

            int num = sourceRect.Width / 3;

            float layerDepth = draw_layer - 0.03f;
            if (draw_layer < 0f)
            {
                draw_layer = 0.8f - (float)y * 1E-06f;
                layerDepth = 0.77f;
            }

            if (drawShadow)
            {
                Color shadowColor = Color.Black * 0.4f;

                b.Draw(texture, new Vector2(x + width - (int)((float)num * scale) - 8, y + 8), new Rectangle(sourceRect.X + num * 2, sourceRect.Y, num, num), shadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth);
                b.Draw(texture, new Vector2(x - 8, y + height - (int)((float)num * scale) + 8), new Rectangle(sourceRect.X, num * 2 + sourceRect.Y, num, num), shadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth);
                b.Draw(texture, new Vector2(x + width - (int)((float)num * scale) - 8, y + height - (int)((float)num * scale) + 8), new Rectangle(sourceRect.X + num * 2, num * 2 + sourceRect.Y, num, num), shadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, layerDepth);

                b.Draw(texture, new Rectangle(x + (int)((float)num * scale) - 8, y + 8, width - (int)((float)num * scale) * 2, (int)((float)num * scale)), new Rectangle(sourceRect.X + num, sourceRect.Y, num, num), shadowColor, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
                b.Draw(texture, new Rectangle(x + (int)((float)num * scale) - 8, y + height - (int)((float)num * scale) + 8, width - (int)((float)num * scale) * 2, (int)((float)num * scale)), new Rectangle(sourceRect.X + num, num * 2 + sourceRect.Y, num, num), shadowColor, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
                b.Draw(texture, new Rectangle(x - 8, y + (int)((float)num * scale) + 8, (int)((float)num * scale), height - (int)((float)num * scale) * 2), new Rectangle(sourceRect.X, num + sourceRect.Y, num, num), shadowColor, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
                b.Draw(texture, new Rectangle(x + width - (int)((float)num * scale) - 8, y + (int)((float)num * scale) + 8, (int)((float)num * scale), height - (int)((float)num * scale) * 2), new Rectangle(sourceRect.X + num * 2, num + sourceRect.Y, num, num), shadowColor, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);

                b.Draw(texture, new Rectangle((int)((float)num * scale / 2f) + x - 8, (int)((float)num * scale / 2f) + y + 8, width - (int)((float)num * scale), height - (int)((float)num * scale)), new Rectangle(num + sourceRect.X, num + sourceRect.Y, num, num), shadowColor, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            }

            b.Draw(texture, new Rectangle((int)((float)num * scale) + x, (int)((float)num * scale) + y, width - (int)((float)num * scale * 2f), height - (int)((float)num * scale * 2f)), new Rectangle(num + sourceRect.X, num + sourceRect.Y, num, num), color, 0f, Vector2.Zero, SpriteEffects.None, draw_layer);

            b.Draw(texture, new Vector2(x, y), new Rectangle(sourceRect.X, sourceRect.Y, num, num), color, 0f, Vector2.Zero, scale, SpriteEffects.None, draw_layer);
            b.Draw(texture, new Vector2(x + width - (int)((float)num * scale), y), new Rectangle(sourceRect.X + num * 2, sourceRect.Y, num, num), color, 0f, Vector2.Zero, scale, SpriteEffects.None, draw_layer);
            b.Draw(texture, new Vector2(x, y + height - (int)((float)num * scale)), new Rectangle(sourceRect.X, num * 2 + sourceRect.Y, num, num), color, 0f, Vector2.Zero, scale, SpriteEffects.None, draw_layer);
            b.Draw(texture, new Vector2(x + width - (int)((float)num * scale), y + height - (int)((float)num * scale)), new Rectangle(sourceRect.X + num * 2, num * 2 + sourceRect.Y, num, num), color, 0f, Vector2.Zero, scale, SpriteEffects.None, draw_layer);

            b.Draw(texture, new Rectangle(x + (int)((float)num * scale), y, width - (int)((float)num * scale) * 2, (int)((float)num * scale)), new Rectangle(sourceRect.X + num, sourceRect.Y, num, num), color, 0f, Vector2.Zero, SpriteEffects.None, draw_layer);
            b.Draw(texture, new Rectangle(x + (int)((float)num * scale), y + height - (int)((float)num * scale), width - (int)((float)num * scale) * 2, (int)((float)num * scale)), new Rectangle(sourceRect.X + num, num * 2 + sourceRect.Y, num, num), color, 0f, Vector2.Zero, SpriteEffects.None, draw_layer);
            b.Draw(texture, new Rectangle(x, y + (int)((float)num * scale), (int)((float)num * scale), height - (int)((float)num * scale) * 2), new Rectangle(sourceRect.X, num + sourceRect.Y, num, num), color, 0f, Vector2.Zero, SpriteEffects.None, draw_layer);
            b.Draw(texture, new Rectangle(x + width - (int)((float)num * scale), y + (int)((float)num * scale), (int)((float)num * scale), height - (int)((float)num * scale) * 2), new Rectangle(sourceRect.X + num * 2, num + sourceRect.Y, num, num), color, 0f, Vector2.Zero, SpriteEffects.None, draw_layer);
        }

        public static void DrawCard(SpriteBatch b, Rectangle bounds, Color color)
        {
            DrawCard(b, bounds.X, bounds.Y, bounds.Width, bounds.Height, color, 1f, false, -1f);
        }
    }
}
