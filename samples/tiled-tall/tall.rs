enum World {
    Width = 16,
    StreamY = 0,
    Height = 40
}

enum Scroll {
    MaxY = 176
}

void main() {
    video.Init();

    world.Load("tall.tmj");
    camera.Init(World.Width, World.StreamY, World.Height);

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
