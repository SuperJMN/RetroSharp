enum World {
    Width = 40,
    StreamY = 10,
    Height = 4,
    CameraLimit = 80,
    SignedVelocityWrap = 128
}

enum Player {
    ScreenX = 72,
    GroundY = 88,
    JumpVelocity = 252,
    Gravity = 1
}

void setup_video() {
    palette.Background(0, 0, 1, 2, 3);
    palette.Sprite(0, 0, 1, 2, 3);
    sprite.Asset(mario_player, "assets/mario-player.png", 18, 32);
    return;
}

void setup_world() {
    world.Column(0, 0, 0, 1, 1);
    world.Column(1, 0, 0, 1, 1);
    world.Column(2, 0, 0, 2, 2);
    world.Column(3, 0, 0, 2, 2);
    world.Column(4, 0, 0, 3, 3);
    world.Column(5, 0, 0, 3, 3);
    world.Column(6, 0, 0, 4, 4);
    world.Column(7, 0, 0, 4, 4);
    world.Column(8, 0, 0, 1, 1);
    world.Column(9, 0, 0, 1, 1);
    world.Column(10, 0, 0, 2, 2);
    world.Column(11, 0, 0, 2, 2);
    world.Column(12, 0, 0, 3, 3);
    world.Column(13, 0, 0, 3, 3);
    world.Column(14, 0, 0, 4, 4);
    world.Column(15, 0, 0, 4, 4);
    world.Column(16, 0, 0, 1, 1);
    world.Column(17, 0, 0, 1, 1);
    world.Column(18, 0, 0, 2, 2);
    world.Column(19, 0, 0, 2, 2);
    world.Column(20, 0, 0, 3, 3);
    world.Column(21, 0, 0, 3, 3);
    world.Column(22, 0, 0, 4, 4);
    world.Column(23, 0, 0, 4, 4);
    world.Column(24, 0, 0, 1, 1);
    world.Column(25, 0, 0, 1, 1);
    world.Column(26, 0, 0, 2, 2);
    world.Column(27, 0, 0, 2, 2);
    world.Column(28, 0, 0, 3, 3);
    world.Column(29, 0, 0, 3, 3);
    world.Column(30, 0, 0, 4, 4);
    world.Column(31, 0, 0, 4, 4);
    world.Column(32, 0, 0, 1, 1);
    world.Column(33, 0, 0, 1, 1);
    world.Column(34, 0, 0, 2, 2);
    world.Column(35, 0, 0, 2, 2);
    world.Column(36, 0, 0, 3, 3);
    world.Column(37, 0, 0, 3, 3);
    world.Column(38, 0, 0, 4, 4);
    world.Column(39, 0, 0, 4, 4);
    world.Map(World.Width, World.StreamY, World.Height);
    return;
}

void main() {
    video.Init();
    setup_video();
    setup_world();
    camera.Init(World.Width, World.StreamY, World.Height);

    u8 cameraX = 0;
    u8 playerY = Player.GroundY;
    u8 velocityY = 0;
    u8 grounded = 1;
    bool flipX = false;

    loop {
        video.WaitVBlank();
        input.Poll();

        if (button_down(right) != 0) {
            flipX = false;
            if (cameraX < World.CameraLimit) {
                cameraX += 1;
            }
        }

        if (button_down(left) != 0) {
            flipX = true;
            if (cameraX > 0) {
                cameraX -= 1;
            }
        }

        if (button_just_pressed(a) != 0) {
            if (grounded != 0) {
                velocityY = Player.JumpVelocity;
                grounded = 0;
            }
        }

        if (grounded == 0) {
            playerY += velocityY;
            velocityY += Player.Gravity;
            if (velocityY < World.SignedVelocityWrap && playerY >= Player.GroundY) {
                playerY = Player.GroundY;
                velocityY = 0;
                grounded = 1;
            }
        }

        camera.SetPosition(cameraX, 0);
        camera.Apply();
        sprite.Draw(mario_player, Player.ScreenX, playerY, 0, flipX, 0);
    }
}
