package com.simpletools.media;

import android.app.Activity;
import android.app.Fragment;
import android.content.Intent;
import android.os.Bundle;

import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;

// The system image picker for SimpleTools.Media.ImagePicker. A headless fragment starts the picker and receives the
// result (the Unity activity cannot), then copies the chosen image into destPath: content URIs are not files.
public final class ImagePicker {
    public interface Callback {
        void onPicked(String path);
    }

    private ImagePicker() {
    }

    public static void pick(final Activity activity, final String destPath, final Callback callback) {
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                PickerFragment fragment = new PickerFragment();
                fragment.destPath = destPath;
                fragment.callback = callback;
                activity.getFragmentManager().beginTransaction().add(fragment, "SimpleToolsImagePicker").commitAllowingStateLoss();
            }
        });
    }

    public static final class PickerFragment extends Fragment {
        private static final int Request = 0x5170;
        String destPath;
        Callback callback;

        @Override
        public void onCreate(Bundle state) {
            super.onCreate(state);
            if (callback == null) {
                // Recreated by the system without our fields: nobody is waiting any more
                getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
                return;
            }

            Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
            intent.setType("image/*");
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            try {
                startActivityForResult(Intent.createChooser(intent, null), Request);
            } catch (Exception exception) {
                finish(null);
            }
        }

        @Override
        public void onActivityResult(int requestCode, int resultCode, Intent data) {
            if (requestCode != Request) {
                return;
            }

            String result = null;
            if (resultCode == Activity.RESULT_OK && data != null && data.getData() != null) {
                try (InputStream input = getActivity().getContentResolver().openInputStream(data.getData());
                     OutputStream output = new FileOutputStream(destPath)) {
                    byte[] buffer = new byte[65536];
                    int read;
                    while ((read = input.read(buffer)) > 0) {
                        output.write(buffer, 0, read);
                    }

                    result = destPath;
                } catch (Exception exception) {
                    result = null;
                }
            }

            finish(result);
        }

        private void finish(String result) {
            Callback waiting = callback;
            callback = null;
            getFragmentManager().beginTransaction().remove(this).commitAllowingStateLoss();
            if (waiting != null) {
                waiting.onPicked(result);
            }
        }
    }
}
