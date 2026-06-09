type Pixel = i16;

const WorldWidth = 8;
const WorldStreamY = 10;
const WorldHeight = 4;
const MarkerScreenX = 72;
const MarkerScreenY = 72;

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
    world.Map(WorldWidth, WorldStreamY, WorldHeight);
    camera.Init(WorldWidth, WorldStreamY, WorldHeight);
    sprite.Asset(marker, "marker.json");

    loop {
        video.WaitVBlank();
        input.Poll();
        let cameraX = button_hold_ticks(right);
        camera.SetPosition(cameraX, 0);
        camera.Apply();
        sprite.Draw(marker, MarkerScreenX, MarkerScreenY, 0, false, 0);
    }
}
