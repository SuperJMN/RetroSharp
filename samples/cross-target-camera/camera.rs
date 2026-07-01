static class Level {
    const i16 Width = 8;
    const i16 StreamY = 10;
    const i16 Height = 4;
}

static class Marker {
    const i16 ScreenX = 72;
    const i16 ScreenY = 72;
}

void Main() {
    Video.Init();

    World.Column(0, 1, 2, 3, 4);
    World.Column(1, 2, 3, 4, 5);
    World.Column(2, 3, 4, 5, 1);
    World.Column(3, 4, 5, 1, 2);
    World.Column(4, 5, 1, 2, 3);
    World.Column(5, 1, 2, 3, 4);
    World.Column(6, 2, 3, 4, 5);
    World.Column(7, 3, 4, 5, 1);
    World.Flags(0, 0, 0, 1, 1);
    World.Flags(1, 0, 0, 1, 1);
    World.Flags(2, 0, 0, 1, 1);
    World.Flags(3, 0, 0, 1, 1);
    World.Flags(4, 0, 0, 1, 1);
    World.Flags(5, 0, 0, 1, 1);
    World.Flags(6, 0, 0, 1, 1);
    World.Flags(7, 0, 0, 1, 1);
    World.Map(Level.Width, Level.StreamY, Level.Height);
    Camera.Init(Level.Width, Level.StreamY, Level.Height);
    Sprite.Asset(marker, "marker.json");

    loop {
        Video.WaitVBlank();
        Input.Poll();
        let cameraX = Input.HoldTicks(Button.Right);
        u8 frame = 0;
        bool flipX = false;
        Camera.SetPosition(cameraX, 0);
        Camera.Apply();
        Sprite.Draw(marker, Marker.ScreenX, Marker.ScreenY, frame, flipX, 0);
    }
}
