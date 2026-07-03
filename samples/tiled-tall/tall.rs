import RetroSharp.Portable2D;

static class Level {
    const i16 Width = 16;
    const i16 StreamY = 0;
    const i16 Height = 40;
}

static class Scroll {
    const i16 MaxY = 176;
}

void Main() {
    Video.Init();

    World.Load("tall.tmj");
    Camera.Init(Level.Width, Level.StreamY, Level.Height);

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
