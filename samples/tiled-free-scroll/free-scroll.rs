import RetroSharp.Portable2D;

static class Level {
    const i16 Width = 50;
    const i16 StreamY = 0;
    const i16 Height = 60;
}

void Main() {
    Video.Init();

    World.Load("free-scroll.tmj");
    Camera.Init(Level.Width, Level.StreamY, Level.Height);

    u8 cameraX = 0;
    u8 cameraY = 0;

    loop {
        Video.WaitVBlank();

        cameraX += 1;
        cameraY += 1;
        Camera.SetPosition(cameraX, cameraY);
        Camera.Apply();
    }
}
