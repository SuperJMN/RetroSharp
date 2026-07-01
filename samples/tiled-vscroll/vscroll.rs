static class Level {
    const i16 Width = 40;
    const i16 StreamY = 0;
    const i16 Height = 60;
}

static class Scroll {
    const i16 MaxY = 240;
    const i16 HoldFrames = 32;
}

void main() {
    Video.Init();

    World.Load("vscroll.tmj");
    Camera.Init(Level.Width, Level.StreamY, Level.Height);

    u8 cameraY = 0;
    u8 direction = 1;
    u8 holdTicks = 0;

    loop {
        Video.WaitVBlank();
        Camera.SetPosition(0, cameraY);
        Camera.Apply();

        if (direction == 1) {
            if (cameraY < Scroll.MaxY) {
                cameraY += 1;
            } else if (holdTicks < Scroll.HoldFrames) {
                holdTicks += 1;
            } else {
                holdTicks = 0;
                direction = 0;
                cameraY -= 1;
            }
        } else {
            if (cameraY > 0) {
                cameraY -= 1;
            } else {
                direction = 1;
                cameraY += 1;
            }
        }
    }
}
