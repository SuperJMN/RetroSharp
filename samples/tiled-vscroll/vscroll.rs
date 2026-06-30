static class World {
    const i16 Width = 40;
    const i16 StreamY = 0;
    const i16 Height = 60;
}

static class Scroll {
    const i16 MaxY = 240;
    const i16 HoldFrames = 32;
}

void main() {
    video.Init();

    world.Load("vscroll.tmj");
    camera.Init(World.Width, World.StreamY, World.Height);

    u8 cameraY = 0;
    u8 direction = 1;
    u8 holdTicks = 0;

    loop {
        video.WaitVBlank();
        camera.SetPosition(0, cameraY);
        camera.Apply();

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
