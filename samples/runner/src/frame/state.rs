using Runner.Camera;
using Runner.Level;
using Runner.Player;

class FrameState
{
    u8 respawnPhase;

    inline void ResolveLanding(PlayerState player, Pixel screenX, Pixel previousFootWorldY, Pixel footWorldY)
    {
        if (player.velocityY >= 0)
        {
            i16 footTile = Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Landable);
            if (footTile >= 0 && previousFootWorldY <= footTile && footWorldY >= footTile)
            {
                player.Land(footTile - Player.FootOffset);
            }
            else
            {
                player.grounded = false;
            }
        }
    }

    inline void ResolveFall(PlayerState player, CameraState view)
    {
        if (!player.grounded && player.y >= Player.FallResetY)
        {
            respawnPhase = 1;
            view.ResetMotion();
            player.x = view.x + Player.StartX;
            player.y = view.y + Player.StartY - Camera.VerticalScrollMax();
        }
    }

    inline void ResolveCeilingHit(PlayerState player, Pixel screenX, Pixel footWorldY)
    {
        if (player.velocityY < 0)
        {
            i16 headProbeY = footWorldY - CollisionProbe.CeilingProbeTopOffset;
            if (Camera.AabbTiles(screenX, headProbeY, Sprite.Width(mario_player), CollisionProbe.CeilingProbeHeight, CollisionFlag.Solid) != 0)
            {
                player.BounceDown();
            }
        }
    }

    inline pure bool IsRespawning() => respawnPhase != 0;

    void AdvanceRespawn(PlayerState player, CameraState view)
    {
        if (view.x > 4)
        {
            view.x -= 4;
            player.x -= 4;
        }
        else if (view.x > 0)
        {
            view.x = 0;
            player.x = Player.StartX;
        }
        else
        {
            let spawnY = Camera.VerticalScrollMax();
            if (view.y < spawnY)
            {
                view.y += 4;
                player.y += 4;
                if (view.y > spawnY)
                {
                    view.y = spawnY;
                    player.y = Player.StartY;
                }
            }
            else
            {
                respawnPhase += 1;
                if (respawnPhase >= 4)
                {
                    player.x = Player.StartX;
                    player.displayFrame = 0;
                    player.Land(Player.StartY);
                    respawnPhase = 0;
                }
            }
        }

    }

}

inline void PresentFrame(PlayerState player, CameraState view)
{
    Pixel screenX = view.ScreenX(player);
    Pixel screenY = view.ScreenY(player);
    Sprite.Draw(mario_player, screenX, screenY, player.displayFrame, player.displayFlipX, 0);
}
