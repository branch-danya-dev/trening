# BodyParts3D mapping inventory

Checked against official Release 4.0 IS-A names and element-file metadata on 2026-10-09. BP representation IDs are not OBJ filenames: the FMA → FJ element mapping below is the build input. The machine-readable [mapping](assets/bodyparts3d/mapping.json) and [manifest](assets/bodyparts3d/manifest.json) pin all hashes. PART-OF lacks most selected muscles; only IS-A is used.

| Internal group | Segment | Mapping / fallback |
|---|---|---|
| pectoralis | torso | FJ1446 (FMA45874, R): abdominal part of right pectoralis major<br>FJ1446M (FMA45875, L): abdominal part of left pectoralis major<br>FJ1447 (FMA34690, R): clavicular part of right pectoralis major<br>FJ1447M (FMA34691, L): clavicular part of left pectoralis major<br>FJ1464 (FMA79979, R): sternocostal part of right pectoralis major<br>FJ1464M (FMA79980, L): sternocostal part of left pectoralis major |
| anterior-deltoid | upperarm | FJ1468 (FMA34680, R): clavicular part of right deltoid<br>FJ1468M (FMA34681, L): clavicular part of left deltoid |
| lateral-deltoid | upperarm | FJ1467 (FMA34682, R): acromial part of right deltoid<br>FJ1467M (FMA34683, L): acromial part of left deltoid |
| posterior-deltoid | upperarm | FJ1513 (FMA34684, R): spinal part of right deltoid<br>FJ1513M (FMA34685, L): spinal part of left deltoid |
| lats | torso | No exact latissimus dorsi concept found in pinned Release 4.0 IS-A metadata; do not substitute trapezius. |
| traps | torso | FJ1520 (FMA33581, R): ascending part of right trapezius<br>FJ1520M (FMA33583, L): ascending part of left trapezius<br>FJ1521 (FMA33586, R): descending part of right trapezius<br>FJ1521M (FMA33587, L): descending part of left trapezius<br>FJ1554 (FMA33584, R): transverse part of right trapezius<br>FJ1554M (FMA33585, L): transverse part of left trapezius |
| rhomboids | torso | FJ1536 (FMA13381, R): right rhomboid major<br>FJ1536M (FMA13382, L): left rhomboid major<br>FJ1537 (FMA13383, R): right rhomboid minor<br>FJ1537M (FMA13384, L): left rhomboid minor |
| biceps | upperarm | FJ1478 (FMA37686, R): long head of right biceps brachii<br>FJ1478M (FMA37687, L): long head of left biceps brachii<br>FJ1512 (FMA37684, R): short head of right biceps brachii<br>FJ1512M (FMA37685, L): short head of left biceps brachii |
| triceps | upperarm | FJ1477 (FMA37697, R): lateral head of right triceps brachii<br>FJ1477M (FMA37698, L): lateral head of left triceps brachii<br>FJ1479 (FMA37699, R): long head of right triceps brachii<br>FJ1479M (FMA37700, L): long head of left triceps brachii<br>FJ1480 (FMA37695, R): medial head of right triceps brachii<br>FJ1480M (FMA37696, L): medial head of left triceps brachii |
| forearms | forearm | Secondary group deferred; existing procedural provider has no direct field. |
| rectus-abdominis | torso | Secondary group deferred; existing procedural provider has no direct field. |
| obliques | torso | Secondary group deferred; existing procedural provider has no direct field. |
| erectors | torso | Secondary group deferred; existing procedural provider has no direct field. |
| glute-max | pelvis | FJ1418 (FMA22328, R): right gluteus maximus<br>FJ1418M (FMA22329, L): left gluteus maximus |
| glute-med | pelvis | FJ1419 (FMA22330, R): right gluteus medius<br>FJ1419M (FMA22331, L): left gluteus medius |
| quadriceps | thigh | FJ1433 (FMA38928, R): right rectus femoris<br>FJ1433M (FMA38929, L): left rectus femoris<br>FJ1441 (FMA38934, R): right vastus intermedius<br>FJ1441M (FMA38935, L): left vastus intermedius<br>FJ1442 (FMA38930, R): right vastus lateralis<br>FJ1442M (FMA38931, L): left vastus lateralis<br>FJ1443 (FMA38932, R): right vastus medialis<br>FJ1443M (FMA38933, L): left vastus medialis |
| hamstrings | thigh | FJ1395 (FMA45888, R): long head of right biceps femoris<br>FJ1395M (FMA45889, L): long head of left biceps femoris<br>FJ1435 (FMA22448, R): right semimembranosus<br>FJ1435M (FMA22449, L): left semimembranosus<br>FJ1436 (FMA22358, R): right semitendinosus<br>FJ1436M (FMA22359, L): left semitendinosus<br>FJ1444 (FMA45891, R): short head of right biceps femoris<br>FJ1444M (FMA45892, L): short head of left biceps femoris |
| calves | calf | FJ1394 (FMA45960, R): lateral head of right gastrocnemius<br>FJ1394M (FMA45961, L): lateral head of left gastrocnemius<br>FJ1397 (FMA45957, R): medial head of right gastrocnemius<br>FJ1397M (FMA45958, L): medial head of left gastrocnemius<br>FJ1437 (FMA22558, R): right soleus<br>FJ1437M (FMA22559, L): left soleus |
| adductors | thigh | Secondary group deferred; existing procedural provider has no direct field. |
| hip-flexors | pelvis | Secondary group deferred; existing procedural provider has no direct field. |

L/R source meshes are both read, folded into a common reference frame, and mirrored onto the symmetric surface. Multiple listed objects are aggregated; this does not allocate muscle mass. Direct mapping means an anatomical object exists, not that its resulting surface field has passed the quality gate. Secondary groups remain no-direct-field (the existing procedural provider also emits zero for them). Lats explicitly retain the procedural field. No substitute is labelled latissimus dorsi.

## Bone and registration inventory

| Object | Concept | Side | Name |
|---|---|---|---|
| FJ3262 | FMA23131 | L | left humerus |
| FJ3368 | FMA23130 | R | right humerus |
| FJ3277 | FMA23465 | L | left radius |
| FJ3349 | FMA23464 | R | right radius |
| FJ3259 | FMA24475 | L | left femur |
| FJ3365 | FMA24474 | R | right femur |
| FJ3282 | FMA24478 | L | left tibia |
| FJ3387 | FMA24477 | R | right tibia |
| FJ3237 | FMA13323 | L | left clavicle |
| FJ3362 | FMA13322 | R | right clavicle |
| FJ3393 | FMA16202 | bilateral | sacrum |
| FJ3288 | FMA16587 | L | left hip bone |
| FJ3152 | FMA16586 | R | right hip bone |
| FJ3172 | FMA12525 | bilateral | seventh cervical vertebra |
| FJ3158 | FMA9165 | bilateral | first thoracic vertebra |
| FJ3156 | FMA10081 | bilateral | twelfth thoracic vertebra |
| FJ3168 | FMA13076 | bilateral | fifth lumbar vertebra |

Source tables: [IS-A names](https://dbarchive.biosciencedbc.jp/data/bodyparts3d/LATEST/isa_parts_list_e.txt), [FMA to element files](https://dbarchive.biosciencedbc.jp/data/bodyparts3d/LATEST/isa_element_parts.txt). Their byte hashes are recorded in mapping.json. This inventory is a geometry correspondence, not a clinical muscle segmentation.
