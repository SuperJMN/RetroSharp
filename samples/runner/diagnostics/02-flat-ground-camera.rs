void SetupVideo() {
    Video.Init();
    Palette.Set(0, 0);
    Palette.Set(1, 1);
    Palette.Set(2, 2);
    Palette.Set(3, 3);
    ObjectPalette.Set(0, 0);
    ObjectPalette.Set(1, 0);
    ObjectPalette.Set(2, 1);
    ObjectPalette.Set(3, 3);
    Sprite.Asset(mario_player, "../assets/mario-player.gb.png", 18, 32);
    Animation.Clip(run, 1, 6, 6, 6);
    return;
}

void DrawBackground() {
    Tilemap.Set(2, 4, 1);
    Tilemap.Set(3, 4, 1);
    Tilemap.Set(4, 5, 1);
    Tilemap.Set(11, 3, 1);
    Tilemap.Set(12, 3, 1);
    Tilemap.Set(13, 4, 1);
    Tilemap.Set(22, 4, 1);
    Tilemap.Set(23, 4, 1);
    Tilemap.Set(24, 5, 1);
    Tilemap.Set(1, 8, 2);
    Tilemap.Set(2, 8, 2);
    Tilemap.Set(3, 8, 2);
    Tilemap.Set(9, 8, 2);
    Tilemap.Set(10, 7, 2);
    Tilemap.Set(11, 8, 2);
    Tilemap.Set(16, 8, 2);
    Tilemap.Set(17, 8, 2);
    Tilemap.Set(25, 8, 2);
    Tilemap.Set(26, 8, 2);
    Tilemap.Set(27, 8, 2);
    return;
}

void DefineWorld() {
    World.Column(0, 0, 0, 0, 0, 4, 5);
    World.Column(1, 0, 0, 0, 0, 4, 5);
    World.Column(2, 0, 0, 0, 0, 4, 5);
    World.Column(3, 0, 0, 0, 0, 4, 5);
    World.Column(4, 0, 0, 0, 0, 4, 5);
    World.Column(5, 0, 0, 0, 0, 4, 5);
    World.Column(6, 0, 0, 0, 0, 4, 5);
    World.Column(7, 0, 0, 0, 0, 4, 5);
    World.Column(8, 0, 0, 0, 0, 4, 5);
    World.Column(9, 0, 0, 0, 0, 4, 5);
    World.Column(10, 0, 0, 0, 0, 4, 5);
    World.Column(11, 0, 0, 0, 0, 4, 5);
    World.Column(12, 0, 0, 0, 0, 4, 5);
    World.Column(13, 0, 0, 0, 0, 4, 5);
    World.Column(14, 0, 0, 0, 0, 4, 5);
    World.Column(15, 0, 0, 0, 0, 4, 5);
    World.Flags(0, 0, 0, 0, 0, 1, 1);
    World.Flags(1, 0, 0, 0, 0, 1, 1);
    World.Flags(2, 0, 0, 0, 0, 1, 1);
    World.Flags(3, 0, 0, 0, 0, 1, 1);
    World.Flags(4, 0, 0, 0, 0, 1, 1);
    World.Flags(5, 0, 0, 0, 0, 1, 1);
    World.Flags(6, 0, 0, 0, 0, 1, 1);
    World.Flags(7, 0, 0, 0, 0, 1, 1);
    World.Flags(8, 0, 0, 0, 0, 1, 1);
    World.Flags(9, 0, 0, 0, 0, 1, 1);
    World.Flags(10, 0, 0, 0, 0, 1, 1);
    World.Flags(11, 0, 0, 0, 0, 1, 1);
    World.Flags(12, 0, 0, 0, 0, 1, 1);
    World.Flags(13, 0, 0, 0, 0, 1, 1);
    World.Flags(14, 0, 0, 0, 0, 1, 1);
    World.Flags(15, 0, 0, 0, 0, 1, 1);
    return;
}

void Main() {
    SetupVideo();
    DrawBackground();
    DefineWorld();
    World.Map(16, 9, 6);
    Camera.Init(16, 9, 6);
    i16 cameraX = 0;
    i16 playerWorldX = 72;
    i16 footTile = 0;
    i16 playerY = 73;
    i16 velocityY = 0;
    i16 grounded = 1;
    i16 displayFrame = 0;
    bool displayFlipX = false;
    i16 animTick = 0;
    i16 jumping = 0;
    i16 jumpTicks = 0;
    i16 moving = 0;
    i16 resetRequested = 0;

    loop {
        Video.WaitVBlank();
        Camera.Apply();
        Sprite.Draw(mario_player, 72, playerY, displayFrame, displayFlipX, 0);

        Input.Poll();

        resetRequested = 0;
        grounded = 0;
        velocityY += 1;
        playerY += velocityY;
        if (velocityY >= 128) {
            if (playerY >= 128) {
                playerY = 0;
                velocityY = 0;
                jumping = 0;
            }
        }

        footTile = 0;
        playerWorldX = cameraX + 72;
        if (playerWorldX >= 128) {
            playerWorldX = playerWorldX - 128;
        }

        if (playerY >= 74) {
            footTile = collision_aabb_tiles(playerWorldX, 32, Sprite.Width(mario_player), 8, 1);

            if (footTile != 0) {
                playerY = 73;
                velocityY = 0;
                grounded = 1;
                jumping = 0;
            }
        }

        if (grounded == 0) {
            if (playerY >= 116) {
                resetRequested = 1;
            }
        }

        if (resetRequested != 0) {
            footTile = 0;
            playerY = 73;
            velocityY = 0;
            grounded = 1;
            displayFrame = 0;
            jumping = 0;
            jumpTicks = 0;
        }

        if (Input.WasPressed(Button.A)) {
            if (grounded != 0) {
                velocityY = 252;
                grounded = 0;
                jumping = 1;
            }
        }

        if (jumping != 0) {
            jumpTicks = Input.HoldTicks(Button.A);
            if (Input.IsDown(Button.A)) {
                if (jumpTicks < 12) {
                    velocityY -= 1;
                }
            }

            if (Input.WasReleased(Button.A)) {
                jumping = 0;
            }
        }

        moving = 0;
        if (Input.IsDown(Button.Right)) {
            moving = 1;
            displayFlipX = false;
            cameraX += 1;
        }

        if (Input.IsDown(Button.Left)) {
            moving = 1;
            displayFlipX = true;
            cameraX -= 1;
        }

        if (moving != 0) {
            Camera.SetPosition(cameraX, 0);
        }

        if (moving != 0) {
            animTick++;
        } else {
            animTick = 0;
        }

        if (grounded == 0) {
            displayFrame = 4;
        } else {
            if (moving != 0) {
                displayFrame = Animation.Frame(run, animTick);
            } else {
                displayFrame = 0;
            }
        }

    }
}
