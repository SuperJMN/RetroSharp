static class World {
    const i16 Width = 50;
    const i16 StreamY = 0;
    const i16 Height = 60;
}

void main() {
    video.Init();

    world.Load("free-scroll.tmj");
    camera.Init(World.Width, World.StreamY, World.Height);

    u8 cameraX = 0;
    u8 cameraY = 0;

    loop {
        video.WaitVBlank();

        cameraX += 1;
        cameraY += 1;
        camera.SetPosition(cameraX, cameraY);
        camera.Apply();
    }
}
