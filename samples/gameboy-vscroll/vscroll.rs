enum World {
    Width = 2,
    StreamY = 0,
    SourceHeight = 24,
    VisibleHeight = 18
}

enum Scroll {
    MaxY = 120
}

void main() {
    video.Init();

    world.Column(0,
        1, 2, 3, 4, 5, 1, 2, 3,
        4, 5, 1, 2, 3, 4, 5, 1,
        2, 3, 4, 5, 1, 2, 3, 4);
    world.Column(1,
        5, 4, 3, 2, 1, 5, 4, 3,
        2, 1, 5, 4, 3, 2, 1, 5,
        4, 3, 2, 1, 5, 4, 3, 2);

    world.Map(World.Width, World.StreamY, World.SourceHeight);
    camera.Init(World.Width, World.StreamY, World.VisibleHeight);

    u8 cameraY = 0;
    u8 direction = 1;

    loop {
        video.WaitVBlank();
        camera.SetPosition(0, cameraY);
        camera.Apply();

        if (direction == 1) {
            if (cameraY < Scroll.MaxY) {
                cameraY += 1;
            } else {
                direction = 0;
            }
        } else {
            if (cameraY > 0) {
                cameraY -= 1;
            } else {
                direction = 1;
            }
        }
    }
}
