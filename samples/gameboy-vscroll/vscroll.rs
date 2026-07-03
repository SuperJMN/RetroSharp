import RetroSharp.Portable2D;

static class Level {
    const i16 Width = 2;
    const i16 StreamY = 0;
    const i16 SourceHeight = 24;
    const i16 VisibleHeight = 18;
}

static class Scroll {
    const i16 MaxY = 120;
}

void Main() {
    Video.Init();

    World.Column(0,
        1, 2, 3, 4, 5, 1, 2, 3,
        4, 5, 1, 2, 3, 4, 5, 1,
        2, 3, 4, 5, 1, 2, 3, 4);
    World.Column(1,
        5, 4, 3, 2, 1, 5, 4, 3,
        2, 1, 5, 4, 3, 2, 1, 5,
        4, 3, 2, 1, 5, 4, 3, 2);

    World.Map(Level.Width, Level.StreamY, Level.SourceHeight);
    Camera.Init(Level.Width, Level.StreamY, Level.VisibleHeight);

    u8 cameraY = 0;
    u8 direction = 1;

    while (true) {
        Video.WaitVBlank();
        Camera.SetPosition(0, cameraY);
        Camera.Apply();

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
