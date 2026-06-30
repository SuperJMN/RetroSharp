static class World {
    const i16 Width = 64;
    const i16 StreamY = 0;
    const i16 Height = 60;
}

static class DeadZone {
    const i16 Left = 64;
    const i16 Right = 96;
    const i16 Top = 56;
    const i16 Bottom = 88;
}

static class CameraBounds {
    const i16 MaxX = 248;
    const i16 MaxY = 240;
}

static class PlayerPath {
    const i16 StartX = 80;
    const i16 StartY = 72;
    const i16 MinX = 80;
    const i16 MaxX = 232;
    const i16 MinY = 72;
    const i16 MaxY = 224;
}

void main() {
    video.Init();

    world.Load("deadzone.tmj");
    camera.Init(World.Width, World.StreamY, World.Height);
    sprite.Asset(marker, "marker.json");

    u8 playerX = PlayerPath.StartX;
    u8 playerY = PlayerPath.StartY;
    u8 cameraX = 0;
    u8 cameraY = 0;
    u8 stepX = 1;
    u8 stepY = 1;

    loop {
        video.WaitVBlank();

        let screenX = playerX - cameraX;
        let screenY = playerY - cameraY;

        if (screenX > DeadZone.Right) {
            if (cameraX < CameraBounds.MaxX) {
                cameraX += 1;
            }
        } else if (screenX < DeadZone.Left) {
            if (cameraX > 0) {
                cameraX -= 1;
            }
        }

        if (screenY > DeadZone.Bottom) {
            if (cameraY < CameraBounds.MaxY) {
                cameraY += 1;
            }
        } else if (screenY < DeadZone.Top) {
            if (cameraY > 0) {
                cameraY -= 1;
            }
        }

        camera.SetPosition(cameraX, cameraY);
        camera.Apply();

        let drawX = playerX - cameraX;
        let drawY = playerY - cameraY;
        sprite.Draw(marker, drawX, drawY, 0, false, 0);

        if (stepX == 1) {
            if (playerX < PlayerPath.MaxX) {
                playerX += 1;
            } else {
                stepX = 255;
            }
        } else {
            if (playerX > PlayerPath.MinX) {
                playerX -= 1;
            } else {
                stepX = 1;
            }
        }

        if (stepY == 1) {
            if (playerY < PlayerPath.MaxY) {
                playerY += 1;
            } else {
                stepY = 255;
            }
        } else {
            if (playerY > PlayerPath.MinY) {
                playerY -= 1;
            } else {
                stepY = 1;
            }
        }
    }
}
