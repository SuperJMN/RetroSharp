enum World {
    Width = 8,
    StreamY = 10,
    Height = 4
}

enum Marker {
    ScreenX = 72,
    ScreenY = 72
}

void main() {
    video.Init();

    world.Column(0, 1, 2, 3, 4);
    world.Column(1, 2, 3, 4, 5);
    world.Column(2, 3, 4, 5, 1);
    world.Column(3, 4, 5, 1, 2);
    world.Column(4, 5, 1, 2, 3);
    world.Column(5, 1, 2, 3, 4);
    world.Column(6, 2, 3, 4, 5);
    world.Column(7, 3, 4, 5, 1);
    world.Flags(0, 0, 0, 1, 1);
    world.Flags(1, 0, 0, 1, 1);
    world.Flags(2, 0, 0, 1, 1);
    world.Flags(3, 0, 0, 1, 1);
    world.Flags(4, 0, 0, 1, 1);
    world.Flags(5, 0, 0, 1, 1);
    world.Flags(6, 0, 0, 1, 1);
    world.Flags(7, 0, 0, 1, 1);
    world.Map(World.Width, World.StreamY, World.Height);
    camera.Init(World.Width, World.StreamY, World.Height);
    sprite.Asset(marker, "marker.json");

    loop {
        video.WaitVBlank();
        input.Poll();
        let cameraX = Input.HoldTicks(Button.Right);
        u8 frame = 0;
        bool flipX = false;
        camera.SetPosition(cameraX, 0);
        camera.Apply();
        sprite.Draw(marker, Marker.ScreenX, Marker.ScreenY, frame, flipX, 0);
    }
}
