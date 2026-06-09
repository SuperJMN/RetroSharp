type Pixel = i16;

const WorldWidth = 8;
const WorldStreamY = 10;
const WorldHeight = 4;
const MarkerScreenX = 72;
const MarkerScreenY = 72;

void main() {
    video_init();

    world_column(0, 1, 2, 3, 4);
    world_column(1, 2, 3, 4, 5);
    world_column(2, 3, 4, 5, 1);
    world_column(3, 4, 5, 1, 2);
    world_column(4, 5, 1, 2, 3);
    world_column(5, 1, 2, 3, 4);
    world_column(6, 2, 3, 4, 5);
    world_column(7, 3, 4, 5, 1);
    world_flags(0, 0, 0, 1, 1);
    world_flags(1, 0, 0, 1, 1);
    world_flags(2, 0, 0, 1, 1);
    world_flags(3, 0, 0, 1, 1);
    world_flags(4, 0, 0, 1, 1);
    world_flags(5, 0, 0, 1, 1);
    world_flags(6, 0, 0, 1, 1);
    world_flags(7, 0, 0, 1, 1);
    world_map(WorldWidth, WorldStreamY, WorldHeight);
    camera_init(WorldWidth, WorldStreamY, WorldHeight);
    sprite_asset(marker, "marker.json");

    Pixel cameraX = 0;

    loop {
        video_wait_vblank();
        input_poll();
        cameraX = button_hold_ticks(right);
        camera_set_position(cameraX, 0);
        camera_apply();
        sprite_draw(marker, MarkerScreenX, MarkerScreenY, 0, false, 0);
    }
}
