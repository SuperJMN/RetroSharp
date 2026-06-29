enum World {
    Width = 64,
    StreamY = 0,
    Height = 60
}

enum DeadZone {
    Left = 64,
    Right = 96,
    Top = 56,
    Bottom = 88
}

enum CameraBounds {
    MaxX = 248,
    MaxY = 240
}

enum PlayerPath {
    StartX = 80,
    StartY = 72,
    MinX = 80,
    MaxX = 232,
    MinY = 72,
    MaxY = 224
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
