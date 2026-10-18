"""Offline checks for explicit and preserved version-2 catalog billboard pins."""
import argparse
import copy
import unittest

from bundle_tiktok import apply_featured_billboard, parse_featured_post


class FeaturedPostTest(unittest.TestCase):
    key = ("1984drh", "7696938454575238430")
    previous = {"featuredCreator": key[0], "featuredPostId": key[1]}

    def catalog(self):
        creator, post_id = self.key
        return {"version": 2, "videos": [{"creator": creator, "id": post_id, "kind": "video",
                "sourceUrl": f"https://www.tiktok.com/@{creator}/video/{post_id}",
                "bundledVideoFile": post_id + ".mp4"}]}

    def test_explicit_pin_and_future_refresh_preserve_identity(self):
        catalog = self.catalog()
        self.assertEqual(apply_featured_billboard(catalog, requested=self.key), self.key)
        refreshed = self.catalog()
        self.assertEqual(apply_featured_billboard(refreshed, catalog), self.key)
        self.assertEqual(refreshed, catalog)
        self.assertEqual(refreshed["version"], 2)

    def test_explicit_replacement_overrides_previous(self):
        catalog = self.catalog()
        old = {"featuredCreator": "machine_publishers", "featuredPostId": "7696449957347331358"}
        apply_featured_billboard(catalog, old, self.key)
        self.assertEqual({k: catalog[k] for k in self.previous}, self.previous)

    def test_absent_or_empty_optional_pair_has_no_effect(self):
        for previous in [None, {}, {"featuredCreator": "", "featuredPostId": ""}]:
            with self.subTest(previous=previous):
                catalog = self.catalog()
                before = copy.deepcopy(catalog)
                self.assertIsNone(apply_featured_billboard(catalog, previous))
                self.assertEqual(catalog, before)

    def test_invalid_identity_pairs_fail_before_mutation(self):
        for requested in [("other", self.key[1]), (self.key[0], "../clip"),
                          (self.key[0], 7696938454575238430), (None, self.key[1]),
                          (self.key[0], ""), self.key[0], self.key + ("extra",)]:
            with self.subTest(requested=requested):
                catalog = self.catalog()
                before = copy.deepcopy(catalog)
                with self.assertRaises(ValueError):
                    apply_featured_billboard(catalog, requested=requested)
                self.assertEqual(catalog, before)

    def test_partial_previous_pair_fails(self):
        with self.assertRaises(ValueError):
            apply_featured_billboard(self.catalog(), {"featuredCreator": self.key[0]})

    def test_removed_or_duplicate_post_fails(self):
        for videos in [[], self.catalog()["videos"] * 2]:
            with self.subTest(videos=videos):
                with self.assertRaises(ValueError):
                    apply_featured_billboard({"videos": videos}, self.previous)

    def test_photos_or_wrong_source_or_path_fail(self):
        for field, value in [("kind", "slideshow"), ("sourceUrl", "https://example.com/video"),
                             ("bundledVideoFile", "../clip.mp4"), ("bundledVideoFile", "other.mp4")]:
            with self.subTest(field=field, value=value):
                catalog = self.catalog()
                catalog["videos"][0][field] = value
                with self.assertRaises(ValueError):
                    apply_featured_billboard(catalog, requested=self.key)

    def test_explicit_cli_requires_creator_and_numeric_id(self):
        self.assertEqual(parse_featured_post("/".join(self.key)), self.key)
        for value in ["https://www.tiktok.com/@1984drh/video/" + self.key[1],
                      "unknown/" + self.key[1], "1984drh/../clip", "1984drh/", "1984drh/123/456"]:
            with self.subTest(value=value):
                with self.assertRaises(argparse.ArgumentTypeError):
                    parse_featured_post(value)


if __name__ == "__main__":
    unittest.main()
