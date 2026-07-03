using Runner.Camera;
using Runner.Level;
using Runner.Player;

class FrameState
{
    bool resetRequested;

    inline void Begin()
    {
        resetRequested = false;
    }

    inline void ResolveSolidLanding(PlayerState player, Pixel screenX, Pixel footWorldY)
    {
        if (player.velocityY > 0)
        {
            let footTile = Camera.AabbHitTop(screenX, footWorldY - CollisionProbe.LandingSearchTopOffset, Sprite.Width(mario_player), CollisionProbe.LandingSearchHeight, CollisionFlag.Solid);
            if (footTile != CollisionProbe.NoTileHit)
            {
                player.Land(footTile - Player.FootOffset);
            }
        }
    }

    inline void ResolveFall(PlayerState player)
    {
        if (!player.grounded)
        {
            if (player.y >= Player.FallResetY)
            {
                resetRequested = true;
            }
        }
    }

    inline void ResolveCeilingHit(PlayerState player, Pixel screenX, Pixel footWorldY)
    {
        if (player.velocityY < 0)
        {
            let headProbeY = footWorldY - CollisionProbe.CeilingProbeTopOffset;
            if (Camera.AabbTiles(screenX, headProbeY, Sprite.Width(mario_player), CollisionProbe.CeilingProbeHeight, CollisionFlag.Solid) != 0)
            {
                player.BounceDown();
            }
        }
    }

    inline void ResolveReset(PlayerState player, CameraState view)
    {
        if (resetRequested)
        {
            player.Reset(view);
            view.ResetMotion();
        }
    }
}

inline void PresentFrame(PlayerState player, CameraState view)
{
    let screenX = view.ScreenX(player);
    let screenY = view.ScreenY(player);
    Video.WaitVBlank();
    Sprite.Draw(mario_player, screenX, screenY, player.displayFrame, player.displayFlipX, 0);
}
