using System.IO;
using UnityEditor;
using UnityEngine;

/// Menu "Island/Export Boat Sheet": every boat look on the open sea (with the sailor on deck) and
/// beached on a sandy shore, saved enlarged as IslandShots/boats.png for checking the art.
/// Batch: Unity -batchmode -quit -projectPath . -executeMethod BoatSheet.Export
public static class BoatSheet
{
    const int CellW = 220, CellH = 200, Cols = 4, Zoom = 2;

    [MenuItem("Island/Export Boat Sheet")]
    public static void Export()
    {
        int rows = (PixelArt.BoatLooks + Cols - 1) / Cols * 2;
        int W = CellW * Cols, H = CellH * rows;
        var buf = new Color[W * H];
        Sprite sea = PixelArt.Water(0), wave = PixelArt.Wave();
        for (int look = 1; look <= PixelArt.BoatLooks; look++)
        {
            int col = (look - 1) % Cols, pair = (look - 1) / Cols;
            int cx = col * CellW, seaY = H - (pair * 2 + 1) * CellH, shoreY = seaY - CellH;
            var boat = PixelArt.Boat(look);
            int deck = Mathf.RoundToInt(PixelArt.BoatDeck(look) * PixelArt.PPU * PixelArt.Detail);

            // open sea: water tiles, a few waves, the boat and the sailor on deck
            Fill(buf, W, cx, seaY, CellW, CellH, PixelArt.WaterColor);
            for (int gy = -4; gy <= 4; gy++)
                for (int gx = -4; gx <= 4; gx++)
                    Blit(buf, W, H, sea, cx + CellW / 2 + (gx - gy) * 32, seaY + 40 - (gx + gy) * 16, cx, seaY);
            for (int i = 0; i < 6; i++) Blit(buf, W, H, wave, cx + 20 + i * 37, seaY + 15 + (i * 53) % 150, cx, seaY);
            Blit(buf, W, H, boat, cx + CellW / 2, seaY + 40, cx, seaY);
            Blit(buf, W, H, PixelArt.Player(0, 0), cx + CellW / 2, seaY + 40 + deck, cx, seaY);

            // the shore: sea to the west (screen left), sand to the east, the boat on the last sand tile
            Fill(buf, W, cx, shoreY, CellW, CellH, PixelArt.WaterColor);
            for (int s = -8; s <= 8; s++)
                for (int gx = -5; gx <= 5; gx++)
                {
                    int gy = s - gx;
                    var tile = gx - gy >= 0 ? PixelArt.Land(gx - gy >= 4, (gx * 7 + gy * 3 & 3), 0) : sea;
                    Blit(buf, W, H, tile, cx + CellW / 2 + (gx - gy) * 32, shoreY + 70 - (gx + gy) * 16, cx, shoreY);
                }
            int seaward = Mathf.RoundToInt((PixelArt.BoatWidth(look) - 40) * 0.4f * PixelArt.Detail); // as IslandGame.BoatShoreOffset
            Blit(buf, W, H, boat, cx + CellW / 2 - seaward, shoreY + 70, cx, shoreY);
            Blit(buf, W, H, PixelArt.Player(0, 0), cx + CellW / 2 + 42, shoreY + 70 - 21, cx, shoreY);
        }
        var tex = new Texture2D(W * Zoom, H * Zoom, TextureFormat.RGBA32, false);
        var outPx = new Color[tex.width * tex.height];
        for (int y = 0; y < tex.height; y++)
            for (int x = 0; x < tex.width; x++) outPx[y * tex.width + x] = buf[(y / Zoom) * W + x / Zoom];
        tex.SetPixels(outPx);
        tex.Apply();
        Directory.CreateDirectory("IslandShots");
        File.WriteAllBytes("IslandShots/boats.png", tex.EncodeToPNG());
        Debug.Log("Exported IslandShots/boats.png");
    }

    static void Fill(Color[] buf, int W, int x0, int y0, int w, int h, Color c)
    {
        for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) buf[y * W + x] = c;
    }

    /// Draw a sprite with its pivot at (px, py) (bottom-left origin), clipped to its cell.
    static void Blit(Color[] buf, int W, int H, Sprite s, int px, int py, int clipX, int clipY)
    {
        var t = s.texture;
        if (!t.isReadable) return;
        var r = s.rect;
        int ox = px - Mathf.RoundToInt(s.pivot.x), oy = py - Mathf.RoundToInt(s.pivot.y);
        for (int y = 0; y < (int)r.height; y++)
            for (int x = 0; x < (int)r.width; x++)
            {
                int bx = ox + x, by = oy + y;
                if (bx < clipX || by < clipY || bx >= clipX + CellW || by >= clipY + CellH || bx >= W || by >= H) continue;
                var c = t.GetPixel((int)r.x + x, (int)r.y + y);
                if (c.a <= 0f) continue;
                int i = by * W + bx;
                var o = Color.Lerp(buf[i], c, c.a); o.a = 1f;
                buf[i] = o;
            }
    }
}
