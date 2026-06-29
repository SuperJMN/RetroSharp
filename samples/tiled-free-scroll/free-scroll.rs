enum World {
    Width = 50,
    StreamY = 0,
    Height = 60
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
